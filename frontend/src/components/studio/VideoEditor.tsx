import { useEffect, useMemo, useState } from 'react';
import {
  ArrowDown, ArrowUp, Clapperboard, Download, Film, Mic, Plus, RefreshCw, Save, Send, Sparkles, Square, Trash2, Users, Volume2, Wand2,
} from 'lucide-react';
import { Badge, Button, Card, Field, Input, Select, Skeleton, Textarea, cn } from '../ui';
import { useConfirm, useToast } from '../feedback';
import {
  BUSY, STATUS_LABEL, StudioCharacter, StudioLocation, StudioMusic, StudioPage, StudioSettings, StudioShot, StudioShotInput, StudioVideo,
  StudioVideoInput, studio, usd,
} from '../../services/studio';
import { navigate } from '../../lib/router';
import { CHINESE_DRAMAS } from '../../data/chineseDramaLibrary';
import { convertScenesToStudioShots, formatProductionScript } from '../../lib/dramaScriptEngine';

/** Facebook only takes Reels up to 90 s; longer videos go up as ordinary Page videos. */
const REELS_MAX_SECONDS = 90;

/**
 * Rough length of a script, for the writer to judge. Understands both "Name: line" and the markdown layout where the
 * speaker sits on its own line ("**Công Tôn Sách:**") and the words follow on the next lines. Speech ≈ 3 Vietnamese
 * words a second + a breath; every other paragraph is an action beat (~3 s); scene headings (#…) add ~2 s.
 * The real length is the sum of the planned shots.
 */
export function estimateScriptSeconds(script: string): { seconds: number; lines: number; actions: number } {
  const clean = (t: string) => t.replace(/[*_`>]/g, '').replace(/\([^)]*\)/g, ' ').trim();
  const words = (t: string) => t.split(/\s+/).filter(Boolean).length;
  let seconds = 0;
  let lines = 0;
  let actions = 0;
  let speaker = false; // the paragraph after a lone "Name:" line is speech
  let inSpeech = false;
  for (const raw of script.split(/\r?\n/)) {
    const trimmed = raw.trim();
    if (!trimmed) {
      inSpeech = false;
      continue;
    }
    if (/^#{1,6}\s/.test(trimmed) || /^-{3,}$/.test(trimmed)) {
      if (/^#/.test(trimmed)) seconds += 2;
      speaker = inSpeech = false;
      continue;
    }
    const line = clean(trimmed);
    if (!line) continue;
    const lone = /^([^:：]{1,40})[:：]$/.exec(line);
    if (lone) {
      speaker = true;
      continue;
    }
    const inline = /^([^:：]{1,40})[:：]\s*(.+)$/.exec(line);
    if (inline && words(inline[1]) <= 5) {
      seconds += words(inline[2]) / 3 + 1;
      lines++;
      speaker = inSpeech = false;
      continue;
    }
    if (speaker || inSpeech) {
      seconds += words(line) / 3 + (inSpeech ? 0 : 1);
      if (!inSpeech) lines++;
      inSpeech = true;
      speaker = false;
      continue;
    }
    seconds += 3;
    actions++;
  }
  return { seconds: Math.round(seconds), lines, actions };
}

/** Seconds an actor needs for a line (same rule as the server's planner: ~3 words a second + 1 s). */
export const secondsToSay = (line?: string | null) =>
  line?.trim() ? Math.ceil(line.trim().split(/\s+/).length / 3 + 1) : 0;

export const fmtDuration = (s: number) => (s >= 60 ? `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')}` : `${Math.round(s)}s`);

const RESOLUTION_LABEL: Record<string, string> = {
  '480p': '480p (xem thử, rẻ nhất)',
  '720p': '720p (khuyên dùng)',
  '1080p': '1080p (nét nhất, đắt nhất)',
};
/** Seedance 1.0 makes clips of 2–12 s. */
const clampSecs = (n: number) => Math.min(12, Math.max(2, n || 0));
const NARRATOR = 'Lời dẫn';
const isNarrator = (name?: string | null) => !name?.trim() || ['lời dẫn', 'người dẫn chuyện', 'dẫn chuyện', 'narrator'].includes(name.trim().toLowerCase());

function statusTone(s: string): 'success' | 'warning' | 'danger' | 'info' | 'neutral' {
  if (s === 'Published' || s === 'Ready' || s === 'Done') return 'success';
  if (s === 'Failed') return 'danger';
  if (BUSY.includes(s as any) || s === 'Generating') return 'warning';
  if (s === 'Planned') return 'info';
  return 'neutral';
}

const toInput = (s: StudioShot): StudioShotInput => ({
  id: s.id, durationSeconds: s.durationSeconds, action: s.action, camera: s.camera, location: s.location ?? '', cast: s.cast ?? [],
  speaker: s.speaker ?? '', dialogue: s.dialogue ?? '', sound: s.sound ?? '',
});

/** Plays one short clip of audio (voice sample / a shot's line) without leaving the page. */
function useAudio() {
  const [playing, setPlaying] = useState<string | null>(null);
  const [el] = useState(() => (typeof Audio !== 'undefined' ? new Audio() : null));
  useEffect(() => {
    if (!el) return;
    const stop = () => setPlaying(null);
    el.addEventListener('ended', stop);
    el.addEventListener('error', stop);
    return () => {
      el.pause();
      el.removeEventListener('ended', stop);
      el.removeEventListener('error', stop);
    };
  }, [el]);
  const toggle = (key: string, url: string) => {
    if (!el) return;
    if (playing === key) {
      el.pause();
      setPlaying(null);
      return;
    }
    el.src = url;
    el.play().then(() => setPlaying(key)).catch(() => setPlaying(null));
  };
  return { playing, toggle };
}

/** Create or work on one video: details → characters & voices → shot plan → clips → final video → Facebook. */
export function VideoEditor({ videoId, onAuthError }: { videoId?: string; onAuthError: (e: unknown) => boolean }) {
  const toast = useToast();
  const confirm = useConfirm();
  const audio = useAudio();
  const isNew = !videoId;

  const [video, setVideo] = useState<StudioVideo | null>(null);
  const [form, setForm] = useState<StudioVideoInput>({
    title: '', script: '', targetDurationSeconds: 30, videoModel: '', aspectRatio: '9:16', resolution: '720p', styleNotes: '',
    musicMode: 'Auto', musicTrackId: null, musicVolume: 0.15, facebookAccountId: null,
  });
  const [characters, setCharacters] = useState<StudioCharacter[]>([]);
  const [locations, setLocations] = useState<StudioLocation[]>([]);
  const [bibleDirty, setBibleDirty] = useState(false);
  const [shots, setShots] = useState<StudioShotInput[]>([]);
  const [shotsDirty, setShotsDirty] = useState(false);
  const [caption, setCaption] = useState('');
  const [pages, setPages] = useState<StudioPage[]>([]);
  const [music, setMusic] = useState<StudioMusic[]>([]);
  const [settings, setSettings] = useState<StudioSettings | null>(null);
  const [remaining, setRemaining] = useState<number | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [showDramaPicker, setShowDramaPicker] = useState(false);

  // Apply a drama episode directly into this Video Editor
  const applyDramaEpisode = (dramaTitle: string, ep: any) => {
    const fullScript = formatProductionScript(ep);
    const convertedShots = convertScenesToStudioShots(ep.scenes);
    setForm((prev) => ({ ...prev, title: `${dramaTitle} - ${ep.title}`, script: fullScript, targetDurationSeconds: 120 }));
    setShots(convertedShots);
    setShotsDirty(true);
    setShowDramaPicker(false);
    toast.success(`Đã nạp ${ep.title} kèm ${convertedShots.length} phân cảnh shot vào editor!`);
  };

  // ── loading ──
  const applyVideo = (v: StudioVideo, resetShots = true) => {
    setVideo(v);
    setForm({
      title: v.title, script: v.script, targetDurationSeconds: v.targetDurationSeconds, videoModel: v.videoModel, aspectRatio: v.aspectRatio,
      resolution: v.resolution, styleNotes: v.styleNotes ?? '', musicMode: v.musicMode, musicTrackId: v.musicTrackId ?? null,
      musicVolume: v.musicVolume, facebookAccountId: v.facebookAccountId ?? null,
    });
    setCaption(v.caption ?? '');
    setCharacters(v.characters ?? []);
    setLocations(v.locations ?? []);
    setBibleDirty(false);
    if (resetShots) {
      setShots(v.shots.map(toInput));
      setShotsDirty(false);
    }
  };

  const refreshVideo = async (resetShots = !shotsDirty) => {
    if (!videoId) return;
    try {
      const v = await studio.video(videoId);
      if (resetShots && !bibleDirty) applyVideo(v, true);
      else setVideo(v);
    } catch (e) {
      toast.error((e as Error).message);
    }
  };

  useEffect(() => {
    (async () => {
      try {
        const [p, m, st, c] = await Promise.all([studio.pages(), studio.music(), studio.settings(), studio.costs()]);
        setPages(p.filter((x) => x.isActive));
        setMusic(m);
        setSettings(st);
        setForm((f) => ({ ...f, videoModel: f.videoModel || st.videoModel }));
        setRemaining(c.monthlyBudgetUsd > 0 ? c.monthRemainingUsd : null);
      } catch (e) {
        toast.error((e as Error).message);
      }
      if (videoId) refreshVideo(true);
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [videoId]);

  // Check for prefilled drama script when creating a new video
  useEffect(() => {
    if (isNew) {
      const prefill = sessionStorage.getItem('studio_prefill_script');
      const prefillTitle = sessionStorage.getItem('studio_prefill_title');
      const prefillShots = sessionStorage.getItem('studio_prefill_shots');
      if (prefill) {
        setForm((prev) => ({ ...prev, script: prefill, title: prefillTitle || prev.title, targetDurationSeconds: 120 }));
        if (prefillShots) {
          try {
            const parsed = JSON.parse(prefillShots);
            if (Array.isArray(parsed) && parsed.length > 0) {
              setShots(parsed.map((x: any) => ({ ...x, id: undefined, cast: Array.isArray(x.cast) ? x.cast : [] })));
              setShotsDirty(true);
            }
          } catch (e) {
            console.error('Lỗi khi nạp shots:', e);
          }
          sessionStorage.removeItem('studio_prefill_shots');
        }
        sessionStorage.removeItem('studio_prefill_script');
        sessionStorage.removeItem('studio_prefill_title');
        toast.success('Đã nạp kịch bản & phân cảnh shot từ kho kịch bản!');
      }
    }
  }, [isNew, toast]);

  // poll while the server works on it
  const serverBusy = !!video && BUSY.includes(video.status);
  useEffect(() => {
    if (!serverBusy) return;
    const t = window.setInterval(() => refreshVideo(!shotsDirty), 4000);
    return () => window.clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [serverBusy, shotsDirty]);

  // a finished background step refreshes the shot list once
  const [lastStatus, setLastStatus] = useState<string | null>(null);
  useEffect(() => {
    if (video && lastStatus && BUSY.includes(lastStatus as any) && !BUSY.includes(video.status)) {
      applyVideo(video, true);
      if (lastStatus === 'Planning' && video.status === 'Planned' && !video.errorMessage)
        toast.success(`Đã có kế hoạch ${video.shots.length} shot và hồ sơ ${video.characters.length} nhân vật — kiểm tra, nghe thử giọng, rồi bấm Tạo video.`);
      if (lastStatus === 'Planning' && video.errorMessage) toast.error(video.errorMessage);
      if (video.status === 'Ready') toast.success('Video đã ghép xong — xem thử bên dưới.');
      if (video.status === 'Published') toast.success('Đã đăng lên Facebook!');
      if (video.status === 'Failed' && video.errorMessage) toast.error(video.errorMessage);
    }
    setLastStatus(video?.status ?? null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [video?.status]);

  const models = settings?.models ?? [];
  const voices = settings?.voices ?? [];
  const voicePrice = settings?.voicePricePerSecondUsd ?? 0.0003;
  const model = models.find((m) => m.id === form.videoModel) ?? models[0];
  const rateOf = (modelId?: string, res?: string) => {
    const m = models.find((x) => x.id === modelId) ?? model;
    return m?.pricePerSecondUsd[res ?? '720p'] ?? 0.02;
  };
  const perSecond = rateOf(form.videoModel, form.resolution);
  const shotCost = (x: { id?: string; durationSeconds: number; dialogue?: string | null }, rate = perSecond) => {
    const hasVoice = !!video?.shots.find((s) => s.id === x.id)?.hasVoice;
    return clampSecs(x.durationSeconds) * rate + (x.dialogue?.trim() && !hasVoice ? secondsToSay(x.dialogue) * voicePrice : 0);
  };
  const shotSeconds = shots.reduce((s, x) => s + clampSecs(x.durationSeconds), 0);
  const estimate = shots.reduce((s, x) => s + shotCost(x), 0);
  const pendingShots = (video?.shots ?? []).filter((s) => !s.hasClip || s.status !== 'Done');
  const pendingSeconds = pendingShots.reduce((s, x) => s + clampSecs(x.durationSeconds), 0);
  const pendingCost = pendingShots.reduce((s, x) => s + shotCost(x), 0);
  const missingVoices = (video?.shots ?? []).filter((s) => s.dialogue?.trim() && !s.hasVoice).length;
  const scriptEstimate = useMemo(() => estimateScriptSeconds(form.script ?? ''), [form.script]);
  const onScreen = characters.filter((c) => !isNarrator(c.name));
  const voiceName = (id: string) => voices.find((v) => v.id === id)?.name ?? id.replace('gemini:', '');

  // ── actions ──
  const guarded = async (label: string, fn: () => Promise<void>) => {
    setBusy(label);
    try {
      await fn();
    } catch (e) {
      if (!onAuthError(e)) toast.error((e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const detailsPayload = (): StudioVideoInput => ({
    ...form, caption, targetDurationSeconds: Math.max(1, scriptEstimate.seconds),
    ...(bibleDirty ? { characters, locations } : {}),
  });

  const saveDetails = () =>
    guarded('save', async () => {
      if (isNew) {
        const v = await studio.createVideo({ ...form, targetDurationSeconds: Math.max(1, scriptEstimate.seconds) });
        // shots loaded from the script library are kept with the new video
        if (shots.length > 0) await studio.saveShots(v.id, shots);
        toast.success(shots.length > 0
          ? 'Đã lưu video kèm phân cảnh — bấm "Lập kế hoạch bằng AI" để AI dựng hồ sơ nhân vật & giọng (hoặc giữ phân cảnh có sẵn).'
          : 'Đã lưu video — bước tiếp theo: bấm "Lập kế hoạch bằng AI" ở mục 2 (chưa tốn tiền tạo clip).');
        navigate(`/studio/videos/${v.id}`);
      } else {
        applyVideo(await studio.updateVideo(videoId!, detailsPayload()), !shotsDirty);
        toast.success('Đã lưu');
      }
    });

  const plan = () =>
    guarded('plan', async () => {
      if (!(await confirm({
        title: 'Lập kế hoạch bằng AI?',
        description: 'Gemini đọc kịch bản để dựng hồ sơ nhân vật (ngoại hình cố định + giọng đọc), bối cảnh, và chia thành các shot. Chi phí vài phần trăm cent. Kế hoạch hiện tại (nếu có) sẽ được thay.',
        confirmLabel: 'Lập kế hoạch',
      }))) return;
      await studio.updateVideo(videoId!, detailsPayload());
      applyVideo(await studio.plan(videoId!), true);
      toast.success('Gemini đang dựng hồ sơ nhân vật và chia shot — kịch bản dài có thể mất 1–3 phút, trang tự cập nhật.');
    });

  const saveShots = () =>
    guarded('shots', async () => {
      applyVideo(await studio.saveShots(videoId!, shots), true);
      toast.success('Đã lưu kế hoạch shot');
    });

  const saveBible = () =>
    guarded('bible', async () => {
      applyVideo(await studio.updateVideo(videoId!, { characters, locations }), !shotsDirty);
      toast.success('Đã lưu hồ sơ nhân vật — câu thoại của nhân vật đổi giọng sẽ được đọc lại.');
    });

  const readVoices = () =>
    guarded('voices', async () => {
      if (shotsDirty) applyVideo(await studio.saveShots(videoId!, shots), true);
      if (bibleDirty) applyVideo(await studio.updateVideo(videoId!, { characters, locations }), true);
      applyVideo(await studio.voices(videoId!), true);
      toast.success('Đang đọc các câu thoại bằng giọng AI — nghe thử ở từng shot khi xong.');
    });

  const rereadShot = (s: StudioShotInput) =>
    guarded('voice-' + s.id, async () => {
      if (shotsDirty) applyVideo(await studio.saveShots(videoId!, shots), true);
      applyVideo(await studio.shotVoice(videoId!, s.id!), true);
    });

  const generate = () =>
    guarded('generate', async () => {
      if (shotsDirty) applyVideo(await studio.saveShots(videoId!, shots), true);
      if (bibleDirty) applyVideo(await studio.updateVideo(videoId!, { characters, locations }), true);
      const fresh = await studio.video(videoId!);
      const todo = fresh.shots.filter((s) => !s.hasClip || s.status !== 'Done');
      const secs = todo.reduce((s, x) => s + clampSecs(x.durationSeconds), 0);
      const cost = todo.reduce((s, x) => s + shotCost(x, rateOf(fresh.videoModel, fresh.resolution)), 0);
      const label = models.find((m) => m.id === fresh.videoModel)?.label ?? fresh.videoModel;
      if (!(await confirm({
        title: `Tạo ${todo.length} clip bằng ${label}?`,
        description: `Khoảng ${secs} giây video ở ${fresh.resolution}, ước tính ${usd(cost)} (Vercel tính theo giây video, cộng vài xu giọng đọc). Câu thoại được đọc trước để clip dài vừa khớp. Clip đã có sẽ không tạo lại. Mỗi clip mất vài phút; trang tự cập nhật.`,
        confirmLabel: `Tạo (~${usd(cost)})`,
      }))) return;
      applyVideo(await studio.generate(videoId!), true);
    });

  const regenerateShot = (s: StudioShotInput) =>
    guarded('shot-' + s.id, async () => {
      if (shotsDirty) applyVideo(await studio.saveShots(videoId!, shots), true);
      if (!(await confirm({ title: 'Tạo lại clip này?', description: `Khoảng ${usd(shotCost(s))}. Clip cũ bị thay; sau đó video được ghép lại.`, confirmLabel: 'Tạo lại' }))) return;
      applyVideo(await studio.generateShot(videoId!, s.id!), true);
    });

  const compose = () => guarded('compose', async () => applyVideo(await studio.compose(videoId!), true));

  const recover = () =>
    guarded('recover', async () => {
      if (!(await confirm({
        title: 'Lấy lại các clip đã trả tiền?',
        description: 'Tải lại những clip đã tạo nhưng bị mất file (server khởi động lại). Miễn phí — không tạo clip mới. Lấy đủ thì tự ghép video; clip nào không còn giữ được sẽ được báo để anh quyết định tạo lại.',
        confirmLabel: 'Lấy lại (miễn phí)',
      }))) return;
      applyVideo(await studio.recoverClips(videoId!), true);
    });

  const publish = () =>
    guarded('publish', async () => {
      const page = pages.find((p) => p.id === form.facebookAccountId);
      if (!page) throw new Error('Chọn Page để đăng ở phần Thông tin rồi bấm Lưu.');
      if (!(await confirm({
        title: `Đăng video lên "${page.pageName}"?`,
        description: 'Video được đăng công khai (Reels nếu ≤ 90 giây). Chú thích sẽ tự kèm dòng "🤖 Video được tạo bằng AI." theo quy định của Meta.',
        confirmLabel: 'Đăng',
      }))) return;
      applyVideo(await studio.publish(videoId!, caption), true);
    });

  const purge = () =>
    guarded('purge', async () => {
      if (!(await confirm({ title: 'Xoá các clip lẻ?', description: 'Giữ video hoàn chỉnh, xoá các clip từng shot để giải phóng dung lượng. Sau đó không tạo lại từng shot được nữa.', confirmLabel: 'Xoá clip' }))) return;
      applyVideo(await studio.purgeClips(videoId!), true);
    });

  const remove = () =>
    guarded('delete', async () => {
      if (!(await confirm({ title: 'Xoá video này?', description: 'Xoá kế hoạch, giọng đọc, mọi clip và video hoàn chỉnh. Không khôi phục được (chi phí đã chi vẫn được ghi lại).', confirmLabel: 'Xoá', danger: true }))) return;
      await studio.deleteVideo(videoId!);
      navigate('/studio');
    });

  // ── bible editing ──
  const updateCharacter = (i: number, patch: Partial<StudioCharacter>) => {
    setCharacters((list) => list.map((c, k) => (k === i ? { ...c, ...patch } : c)));
    setBibleDirty(true);
  };
  const updateLocation = (i: number, patch: Partial<StudioLocation>) => {
    setLocations((list) => list.map((l, k) => (k === i ? { ...l, ...patch } : l)));
    setBibleDirty(true);
  };

  // ── shot editing ──
  const updateShot = (i: number, patch: Partial<StudioShotInput>) => {
    setShots((list) => list.map((s, k) => (k === i ? { ...s, ...patch } : s)));
    setShotsDirty(true);
  };
  const moveShot = (i: number, d: -1 | 1) => {
    setShots((list) => {
      const j = i + d;
      if (j < 0 || j >= list.length) return list;
      const copy = [...list];
      [copy[i], copy[j]] = [copy[j], copy[i]];
      return copy;
    });
    setShotsDirty(true);
  };
  const addShot = () => {
    setShots((list) => [...list, { durationSeconds: 6, action: '', camera: 'medium-wide shot', location: locations[0]?.name ?? '', cast: [], speaker: '', dialogue: '', sound: '' }]);
    setShotsDirty(true);
  };
  const removeShot = (i: number) => {
    setShots((list) => list.filter((_, k) => k !== i));
    setShotsDirty(true);
  };
  const toggleCast = (i: number, name: string) =>
    updateShot(i, { cast: shots[i].cast.includes(name) ? shots[i].cast.filter((n) => n !== name) : [...shots[i].cast, name] });

  if (!isNew && !video) return <Skeleton className="h-96 w-full" />;
  const locked = serverBusy || !!busy;
  const shotState = (id?: string) => video?.shots.find((s) => s.id === id);

  return (
    <div className="space-y-5">
      {video && (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Badge tone={statusTone(video.status)} dot pulse={serverBusy}>
            {STATUS_LABEL[video.status]}
          </Badge>
          {video.progress && <span className="text-fg-muted">{video.progress}</span>}
          <span className="ml-auto text-xs text-fg-muted">
            Đã chi <b className="text-fg">{usd(video.actualCostUsd, 3)}</b>
          </span>
        </div>
      )}
      {video?.errorMessage && !serverBusy && (
        <p className="rounded-lg border border-danger/25 bg-danger/[0.06] p-3 text-xs text-danger">{video.errorMessage}</p>
      )}

      {/* 1. Details */}
      <Card className="space-y-4 p-4">
        <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 border-b border-line pb-3">
          <div className="flex items-center gap-2 text-sm font-semibold text-fg">
            <Clapperboard className="h-4 w-4" /> 1. Thông tin video
          </div>
          <Button variant="secondary" size="sm" icon={<Sparkles className="h-3.5 w-3.5 text-brand" />} onClick={() => setShowDramaPicker(true)}
            className="border-brand/40 text-brand hover:bg-brand/10 text-xs font-medium">
            🎭 Chọn Kịch Bản Phim Miền Tây
          </Button>
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Page sẽ đăng" hint="Tên Page (và logo trong brand kit) được gắn chìm lên video, tự đổi vị trí mỗi 7 giây để chống reup. Đổi Page sau khi ghép thì video tự ghép lại.">
            <Select value={form.facebookAccountId ?? ''} onChange={(e) => setForm({ ...form, facebookAccountId: e.target.value || null })} disabled={locked}>
              <option value="">— Chọn Page —</option>
              {pages.map((p) => (
                <option key={p.id} value={p.id}>{p.pageName}</option>
              ))}
            </Select>
          </Field>
          <Field label="Tên video">
            <Input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} placeholder="vd: Đại nhân cũng biết sợ vợ" disabled={locked} />
          </Field>
        </div>
        <Field label="Kịch bản" hint="Mỗi câu thoại 1 dòng dạng “Tên: lời thoại”, hành động / bối cảnh ở dòng riêng. Video dài đúng theo kịch bản — AI không thêm, không cắt. Lời thoại được đọc bằng giọng AI.">
          <Textarea rows={10} value={form.script} onChange={(e) => setForm({ ...form, script: e.target.value })} disabled={locked} />
        </Field>
        {form.script?.trim() && (
          <div className={cn('rounded-lg border px-3 py-2 text-xs', scriptEstimate.seconds > REELS_MAX_SECONDS ? 'border-warning/40 bg-warning/[0.06]' : 'border-line bg-subtle')}>
            Ước tính theo kịch bản: <b className="text-fg">~{fmtDuration(scriptEstimate.seconds)}</b> ({scriptEstimate.lines} câu thoại, {scriptEstimate.actions} dòng hành động)
            · chi phí ~<b className="text-fg">{usd(scriptEstimate.seconds * perSecond)}</b> ·{' '}
            {scriptEstimate.seconds > REELS_MAX_SECONDS
              ? <span className="text-warning">dài hơn 90 giây → đăng thành video thường, không vào Reels</span>
              : <span className="text-success">≤ 90 giây → đăng Reels</span>}
            . Con số chính xác có sau khi lập kế hoạch shot.
          </div>
        )}
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label="Model tạo video" hint={model?.note}>
            <Select value={form.videoModel ?? ''} onChange={(e) => setForm({ ...form, videoModel: e.target.value })} disabled={locked}>
              {models.map((m) => (
                <option key={m.id} value={m.id}>{m.label}</option>
              ))}
            </Select>
          </Field>
          <Field label="Độ phân giải clip" hint={`${usd(perSecond, 4)}/giây · video cuối luôn xuất 1080p`}>
            <Select value={form.resolution} onChange={(e) => setForm({ ...form, resolution: e.target.value })} disabled={locked}>
              {(['480p', '720p', '1080p'] as const).map((r) => (
                <option key={r} value={r}>{RESOLUTION_LABEL[r]} — {usd(rateOf(form.videoModel, r), 4)}/s</option>
              ))}
            </Select>
          </Field>
          <Field label="Khung hình">
            <Select value={form.aspectRatio} onChange={(e) => setForm({ ...form, aspectRatio: e.target.value })} disabled={locked}>
              <option value="9:16">Dọc 9:16 (Reels)</option>
              <option value="16:9">Ngang 16:9</option>
            </Select>
          </Field>
        </div>
        <Field label="Phong cách / ghi chú cho đạo diễn AI" optional hint="vd: phim cổ trang hài, nắng chiều vàng, màu điện ảnh ấm, máy quay cầm tay nhẹ">
          <Input value={form.styleNotes} onChange={(e) => setForm({ ...form, styleNotes: e.target.value })} disabled={locked} />
        </Field>
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label="Nhạc nền">
            <Select value={form.musicMode} onChange={(e) => setForm({ ...form, musicMode: e.target.value })} disabled={locked}>
              <option value="Auto">AI tự chọn từ kho nhạc</option>
              <option value="Manual">Tự chọn bài</option>
              <option value="None">Không nhạc</option>
            </Select>
          </Field>
          {form.musicMode !== 'None' && (
            <Field label={form.musicMode === 'Auto' ? 'Bài AI đã chọn (đổi được)' : 'Bài nhạc'}>
              <Select value={form.musicTrackId ?? ''} onChange={(e) => setForm({ ...form, musicTrackId: e.target.value || null })} disabled={locked}>
                <option value="">{form.musicMode === 'Auto' ? '— AI chọn khi lập kế hoạch —' : '— Chọn bài —'}</option>
                {music.map((m) => (
                  <option key={m.id} value={m.id}>{m.name}{m.mood ? ` · ${m.mood}` : ''}</option>
                ))}
              </Select>
            </Field>
          )}
          {form.musicMode !== 'None' && (
            <Field label={`Âm lượng nhạc: ${Math.round((form.musicVolume ?? 0.15) * 100)}%`} hint="Nhạc tự nhỏ lại khi có người nói.">
              <input type="range" min={0} max={0.6} step={0.01} value={form.musicVolume ?? 0.15}
                onChange={(e) => setForm({ ...form, musicVolume: Number(e.target.value) })} className="w-full" disabled={locked} />
            </Field>
          )}
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="primary" icon={<Save className="h-4 w-4" />} loading={busy === 'save'} disabled={locked} onClick={saveDetails}>
            {isNew ? 'Lưu và sang bước lập kế hoạch' : 'Lưu thông tin'}
          </Button>
          {isNew && (
            <span className="self-center text-xs text-fg-muted">
              Chưa tốn tiền. Các bước: lưu → lập kế hoạch (vài xu) → nghe thử giọng → <b>tạo video thật</b> (tính tiền theo giây) → đăng Page.
            </span>
          )}
          {!isNew && (
            <Button variant="danger-soft" icon={<Trash2 className="h-4 w-4" />} loading={busy === 'delete'} disabled={locked} onClick={remove}>
              Xoá video
            </Button>
          )}
        </div>
      </Card>

      {!isNew && video && (
        <>
          {/* 2. Characters, locations & voices */}
          <Card className="space-y-4 p-4">
            <div className="flex flex-wrap items-center gap-2">
              <div className="flex items-center gap-2 text-sm font-semibold text-fg">
                <Users className="h-4 w-4" /> 2. Hồ sơ nhân vật, bối cảnh & giọng đọc
              </div>
              <Button className="ml-auto" size="sm" variant="secondary" icon={<Sparkles className="h-4 w-4" />} loading={busy === 'plan'}
                disabled={locked || !form.script?.trim() || video.shots.some((s) => s.hasClip)} onClick={plan}
                title={video.shots.some((s) => s.hasClip) ? 'Đã có clip — tạo video mới nếu muốn lập kế hoạch lại' : ''}>
                {video.shots.length ? 'Lập lại kế hoạch bằng AI' : 'Lập kế hoạch bằng AI'}
              </Button>
            </div>
            <p className="text-xs text-fg-muted">
              Model tạo video từ chữ (không dùng ảnh), nên ngoại hình mỗi nhân vật được mô tả cố định bằng tiếng Anh và chép vào mọi shot để nhân vật trông
              giống nhau. Mô tả càng cụ thể (tuổi, khuôn mặt, râu tóc, trang phục + màu) thì càng đồng nhất. Mỗi nhân vật có một giọng đọc AI riêng.
            </p>
            {characters.length === 0 ? (
              <p className="text-xs text-fg-subtle">Chưa có hồ sơ — bấm "Lập kế hoạch bằng AI" để Gemini dựng từ kịch bản.</p>
            ) : (
              <div className="space-y-2">
                {characters.map((c, i) => (
                  <div key={i} className="grid gap-2 rounded-lg border border-line p-2.5 sm:grid-cols-[150px_100px_1fr_220px]">
                    <Field label="Nhân vật">
                      <Input value={c.name} disabled={locked} onChange={(e) => updateCharacter(i, { name: e.target.value })} />
                    </Field>
                    <Field label="Giới tính">
                      <Select value={c.gender} disabled={locked} onChange={(e) => updateCharacter(i, { gender: e.target.value })}>
                        <option value="male">Nam</option>
                        <option value="female">Nữ</option>
                      </Select>
                    </Field>
                    <Field label={isNarrator(c.name) ? 'Lời dẫn (không xuất hiện trong hình)' : 'Ngoại hình (tiếng Anh, cố định mọi shot)'}>
                      <Textarea rows={2} value={c.look} disabled={locked || isNarrator(c.name)} onChange={(e) => updateCharacter(i, { look: e.target.value })} />
                    </Field>
                    <Field label="Giọng đọc">
                      <div className="flex gap-1">
                        <Select value={c.voice} disabled={locked} onChange={(e) => updateCharacter(i, { voice: e.target.value })}>
                          {voices.map((v) => (
                            <option key={v.id} value={v.id}>{v.name} — {v.description}</option>
                          ))}
                        </Select>
                        <Button size="sm" variant="ghost" icon={audio.playing === 'sample-' + i ? <Square className="h-3.5 w-3.5" /> : <Volume2 className="h-3.5 w-3.5" />}
                          onClick={() => audio.toggle('sample-' + i, studio.voicePreviewUrl(c.voice))} aria-label="Nghe thử giọng" title="Nghe thử giọng" />
                      </div>
                    </Field>
                  </div>
                ))}
              </div>
            )}
            {locations.length > 0 && (
              <div className="space-y-2">
                <div className="text-xs font-semibold text-fg">Bối cảnh</div>
                {locations.map((l, i) => (
                  <div key={i} className="grid gap-2 sm:grid-cols-[150px_1fr]">
                    <Input value={l.name} disabled={locked} onChange={(e) => updateLocation(i, { name: e.target.value })} />
                    <Textarea rows={2} value={l.look} disabled={locked} onChange={(e) => updateLocation(i, { look: e.target.value })} />
                  </div>
                ))}
              </div>
            )}
            <div className="flex flex-wrap gap-2">
              <Button size="sm" variant="secondary" icon={<Plus className="h-4 w-4" />} disabled={locked}
                onClick={() => { setCharacters((l) => [...l, { name: '', gender: 'male', look: '', voice: voices[0]?.id ?? 'gemini:Charon' }]); setBibleDirty(true); }}>
                Thêm nhân vật
              </Button>
              <Button size="sm" variant="secondary" icon={<Plus className="h-4 w-4" />} disabled={locked}
                onClick={() => { setLocations((l) => [...l, { name: '', look: '' }]); setBibleDirty(true); }}>
                Thêm bối cảnh
              </Button>
              <Button size="sm" variant="primary" icon={<Save className="h-4 w-4" />} loading={busy === 'bible'} disabled={locked || !bibleDirty} onClick={saveBible}>
                Lưu hồ sơ
              </Button>
              {bibleDirty && <span className="self-center text-xs text-warning">Có thay đổi chưa lưu</span>}
            </div>
          </Card>

          {/* 3. Shot plan */}
          <Card className="space-y-4 p-4">
            <div className="flex flex-wrap items-center gap-2">
              <div className="flex items-center gap-2 text-sm font-semibold text-fg">
                <Wand2 className="h-4 w-4" /> 3. Kế hoạch shot
              </div>
              <span className="text-xs text-fg-muted">
                {shots.length} shot · dài {fmtDuration(shotSeconds)}{shotSeconds > REELS_MAX_SECONDS ? ' (quá 90s → video thường, không phải Reels)' : ''} · ước tính {usd(estimate)}
              </span>
            </div>
            {video.planNotes && <p className="rounded-lg bg-subtle p-2.5 text-xs text-fg-muted">🎬 {video.planNotes}</p>}
            {shots.length === 0 ? (
              <p className="text-xs text-fg-muted">Chưa có shot. Bấm "Lập kế hoạch bằng AI" ở mục 2 để Gemini chia kịch bản (hoặc thêm tay).</p>
            ) : (
              <ol className="space-y-3">
                {shots.map((s, i) => {
                  const st = shotState(s.id);
                  const needs = secondsToSay(s.dialogue);
                  return (
                    <li key={s.id ?? `new-${i}`} className="rounded-lg border border-line p-3">
                      <div className="mb-2 flex flex-wrap items-center gap-2 text-xs">
                        <span className="font-semibold text-fg">Shot {i + 1}</span>
                        {st && <Badge tone={statusTone(st.status)}>{st.status === 'Done' ? 'Có clip' : st.status === 'Failed' ? 'Lỗi' : st.status === 'Generating' ? 'Đang tạo' : 'Chưa tạo'}</Badge>}
                        {st && st.costUsd > 0 && <span className="text-fg-muted">đã chi {usd(st.costUsd, 3)}</span>}
                        <div className="ml-auto flex gap-1">
                          <Button size="sm" variant="ghost" icon={<ArrowUp className="h-3.5 w-3.5" />} disabled={locked || i === 0} onClick={() => moveShot(i, -1)} aria-label="Lên" />
                          <Button size="sm" variant="ghost" icon={<ArrowDown className="h-3.5 w-3.5" />} disabled={locked || i === shots.length - 1} onClick={() => moveShot(i, 1)} aria-label="Xuống" />
                          {s.id && st && (
                            <Button size="sm" variant="secondary" icon={<RefreshCw className="h-3.5 w-3.5" />} loading={busy === 'shot-' + s.id} disabled={locked} onClick={() => regenerateShot(s)}>
                              {st.hasClip ? 'Tạo lại' : 'Tạo clip'}
                            </Button>
                          )}
                          <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} disabled={locked} onClick={() => removeShot(i)} aria-label="Xoá shot" />
                        </div>
                      </div>
                      {st?.errorMessage && <p className="mb-2 text-xs text-danger">{st.errorMessage}</p>}
                      <div className="grid gap-3 lg:grid-cols-[1fr_200px]">
                        <div className="space-y-2">
                          <div className="grid gap-2 sm:grid-cols-[90px_1fr_2fr]">
                            <Field label="Giây (2–12)">
                              <Input type="number" min={2} max={12} value={s.durationSeconds} disabled={locked}
                                onChange={(e) => updateShot(i, { durationSeconds: Number(e.target.value) || 6 })} />
                            </Field>
                            <Field label="Người nói">
                              <Input list="studio-speakers" value={s.speaker ?? ''} disabled={locked} onChange={(e) => updateShot(i, { speaker: e.target.value })} placeholder="(không thoại)" />
                            </Field>
                            <Field label="Lời thoại (tiếng Việt, đọc bằng giọng AI)">
                              <Input value={s.dialogue ?? ''} disabled={locked} onChange={(e) => updateShot(i, { dialogue: e.target.value })} />
                            </Field>
                          </div>
                          {needs > clampSecs(s.durationSeconds) && !st?.hasVoice && (
                            <p className="text-[11px] text-warning">
                              Câu thoại cần khoảng {Math.min(12, needs)}s — shot sẽ tự dài ra cho vừa giọng đọc.
                              {needs > 12 && ' Câu quá dài cho 1 clip (tối đa 12s): nên tách thành 2 shot.'}
                            </p>
                          )}
                          {st?.hasVoice && s.id && (
                            <div className="flex flex-wrap items-center gap-2 text-[11px] text-fg-muted">
                              <Button size="sm" variant="ghost" icon={audio.playing === 'line-' + s.id ? <Square className="h-3.5 w-3.5" /> : <Volume2 className="h-3.5 w-3.5" />}
                                onClick={() => audio.toggle('line-' + s.id, studio.voiceUrl(s.id!) + `?v=${st.voiceSeconds}`)}>
                                Nghe giọng đọc ({st.voiceSeconds?.toFixed(1)}s)
                              </Button>
                              <span>giọng {voiceName(characters.find((c) => c.name.trim().toLowerCase() === (s.speaker ?? '').trim().toLowerCase())?.voice ?? 'gemini:Charon')}</span>
                              <Button size="sm" variant="ghost" icon={<Mic className="h-3.5 w-3.5" />} loading={busy === 'voice-' + s.id} disabled={locked} onClick={() => rereadShot(s)}>
                                Đọc lại
                              </Button>
                            </div>
                          )}
                          <Field label="Hành động (tiếng Anh để model hiểu tốt nhất)">
                            <Textarea rows={2} value={s.action} disabled={locked} onChange={(e) => updateShot(i, { action: e.target.value })} />
                          </Field>
                          <div className="grid gap-2 sm:grid-cols-3">
                            <Field label="Bối cảnh">
                              <Select value={s.location ?? ''} disabled={locked} onChange={(e) => updateShot(i, { location: e.target.value })}>
                                <option value="">—</option>
                                {locations.map((l) => (
                                  <option key={l.name} value={l.name}>{l.name}</option>
                                ))}
                                {s.location && !locations.some((l) => l.name === s.location) && <option value={s.location}>{s.location}</option>}
                              </Select>
                            </Field>
                            <Field label="Góc máy">
                              <Input value={s.camera} disabled={locked} onChange={(e) => updateShot(i, { camera: e.target.value })} />
                            </Field>
                            <Field label="Âm thanh (ghi chú)">
                              <Input value={s.sound ?? ''} disabled={locked} onChange={(e) => updateShot(i, { sound: e.target.value })} />
                            </Field>
                          </div>
                          {onScreen.length > 0 && (
                            <Field label="Nhân vật có mặt trong khung hình">
                              <div className="flex flex-wrap gap-1.5">
                                {onScreen.map((c) => {
                                  const on = s.cast.includes(c.name);
                                  return (
                                    <button key={c.name} type="button" disabled={locked} onClick={() => toggleCast(i, c.name)}
                                      className={cn('rounded-full border px-2.5 py-0.5 text-[11px] transition-colors', on ? 'border-accent bg-accent text-white' : 'border-line text-fg-muted hover:bg-muted')}>
                                      {on ? '✓ ' : ''}{c.name}
                                    </button>
                                  );
                                })}
                              </div>
                            </Field>
                          )}
                        </div>
                        <div>
                          {st?.hasClip && s.id ? (
                            <video src={studio.clipUrl(s.id) + `?v=${st.attempts}`} controls preload="metadata"
                              className={cn('w-full rounded-lg bg-black', video.aspectRatio === '16:9' ? 'aspect-video' : 'aspect-[9/16]')} />
                          ) : (
                            <div className={cn('flex w-full items-center justify-center rounded-lg bg-muted text-xs text-fg-subtle', video.aspectRatio === '16:9' ? 'aspect-video' : 'aspect-[9/16]')}>
                              {st?.status === 'Generating' ? 'Đang tạo…' : 'Chưa có clip'}
                            </div>
                          )}
                        </div>
                      </div>
                    </li>
                  );
                })}
              </ol>
            )}
            <datalist id="studio-speakers">
              {characters.map((c) => (
                <option key={c.name} value={c.name} />
              ))}
              <option value={NARRATOR} />
            </datalist>
            <div className="flex flex-wrap gap-2">
              <Button size="sm" variant="secondary" icon={<Plus className="h-4 w-4" />} disabled={locked} onClick={addShot}>
                Thêm shot
              </Button>
              <Button size="sm" variant="primary" icon={<Save className="h-4 w-4" />} loading={busy === 'shots'} disabled={locked || !shotsDirty} onClick={saveShots}>
                Lưu kế hoạch
              </Button>
              {shotsDirty && <span className="self-center text-xs text-warning">Có thay đổi chưa lưu</span>}
            </div>
          </Card>

          {/* 4. Generate + final */}
          <Card className="space-y-4 p-4">
            <div className="flex items-center gap-2 text-sm font-semibold text-fg">
              <Film className="h-4 w-4" /> 4. Tạo video
            </div>
            {video.shots.length === 0 ? (
              <p className="rounded-lg border border-warning/40 bg-warning/[0.06] px-3 py-2 text-xs text-fg">
                Chưa có shot nào — bấm <b>"Lập kế hoạch bằng AI"</b> ở mục 2 (hoặc thêm shot tay rồi <b>Lưu kế hoạch</b>) trước, rồi mới tạo video được.
              </p>
            ) : (
              <p className="text-xs text-fg-muted">
                Mỗi shot là một <b>đoạn video chuyển động như phim</b> do <b>{models.find((m) => m.id === video.videoModel)?.label ?? video.videoModel}</b> tạo từ mô tả,
                ở <b>{video.resolution}</b> ({usd(rateOf(video.videoModel, video.resolution), 4)}/giây). Video không có tiếng: lời thoại được đọc bằng giọng AI rồi
                lồng vào, thêm nhạc nền, ghép liền và xuất 1080p. Còn {pendingSeconds}s chưa tạo, ước tính <b>{usd(pendingCost)}</b>
                {remaining != null && <> · ngân sách tháng còn <b>{usd(remaining)}</b></>}.
              </p>
            )}
            <div className="flex flex-wrap gap-2">
              {missingVoices > 0 && (
                <Button variant="secondary" icon={<Mic className="h-4 w-4" />} loading={busy === 'voices'} disabled={locked} onClick={readVoices}
                  title="Đọc trước các câu thoại để nghe thử giọng — gần như miễn phí, chưa tốn tiền video">
                  Nghe thử giọng {missingVoices} câu thoại (~{usd(missingVoices * 4 * voicePrice, 3)})
                </Button>
              )}
              <Button variant="primary" icon={<Sparkles className="h-4 w-4" />} loading={busy === 'generate'} disabled={locked || shots.length === 0} onClick={generate}>
                {video.shots.some((s) => s.hasClip) ? 'Tạo các clip còn thiếu' : 'Tạo video'}
              </Button>
              <Button variant="secondary" icon={<RefreshCw className="h-4 w-4" />} loading={busy === 'compose'}
                disabled={locked || video.shots.length === 0 || video.shots.some((s) => !s.hasClip)} onClick={compose}>
                Ghép lại (đổi nhạc / thứ tự / giọng)
              </Button>
              {(video.status === 'Composing' || video.status === 'Failed' || video.shots.some((s) => s.errorMessage?.includes('mất'))) && (
                <Button variant="secondary" icon={<RefreshCw className="h-4 w-4" />} loading={busy === 'recover'}
                  disabled={!!busy || (serverBusy && video.status !== 'Composing')} onClick={recover}
                  title="Tải lại các clip đã trả tiền nhưng bị mất file — không tốn tiền">
                  Lấy lại clip đã trả tiền (miễn phí)
                </Button>
              )}
            </div>
            {video.hasFinal && (
              <div className="space-y-2">
                <video key={video.finalDurationSeconds ?? 0} src={studio.finalUrl(video.id) + `?t=${encodeURIComponent(video.finalDurationSeconds ?? 0)}`} controls preload="metadata"
                  className={cn('mx-auto rounded-lg bg-black', video.aspectRatio === '16:9' ? 'aspect-video w-full' : 'aspect-[9/16] max-h-[70vh]')} />
                <div className="flex flex-wrap items-center gap-2 text-xs text-fg-muted">
                  <span>Dài {video.finalDurationSeconds}s{video.musicName ? ` · nhạc: ${video.musicName}` : ''}</span>
                  <a href={studio.finalUrl(video.id, true)} className="ml-auto inline-flex items-center gap-1 text-accent hover:underline">
                    <Download className="h-3.5 w-3.5" /> Tải về
                  </a>
                </div>
              </div>
            )}
          </Card>

          {/* 5. Publish */}
          <Card className="space-y-3 p-4">
            <div className="flex items-center gap-2 text-sm font-semibold text-fg">
              <Send className="h-4 w-4" /> 5. Đăng lên Facebook
            </div>
            <Field label="Chú thích" hint='Tự kèm dòng "🤖 Video được tạo bằng AI." khi đăng. Nhớ bật nhãn AI nếu Facebook hỏi.'>
              <Textarea rows={3} value={caption} onChange={(e) => setCaption(e.target.value)} disabled={locked} />
            </Field>
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="primary" icon={<Send className="h-4 w-4" />} loading={busy === 'publish'} disabled={locked || !video.hasFinal || !form.facebookAccountId} onClick={publish}>
                Đăng lên {pages.find((p) => p.id === form.facebookAccountId)?.pageName ?? 'Page'}
              </Button>
              {video.hasFinal && video.shots.some((s) => s.hasClip) && (
                <Button variant="ghost" size="sm" loading={busy === 'purge'} disabled={locked} onClick={purge}>
                  Xoá clip lẻ (giải phóng dung lượng)
                </Button>
              )}
              {video.publishedPostId && (
                <a className="text-xs text-accent hover:underline" href={`https://www.facebook.com/${video.publishedPostId}`} target="_blank" rel="noopener noreferrer">
                  Xem bài đã đăng ↗
                </a>
              )}
            </div>
          </Card>
        </>
      )}

      {/* Drama Scripts Picker Modal */}
      {showDramaPicker && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm">
          <Card className="max-h-[85vh] w-full max-w-2xl overflow-y-auto p-5 space-y-4 shadow-2xl">
            <div className="flex items-center justify-between border-b border-line pb-3">
              <div>
                <h4 className="font-bold text-fg text-base flex items-center gap-2">
                  <Sparkles className="h-4 w-4 text-brand" /> Kho Kịch Bản Phim Miền Tây & Việt Hóa
                </h4>
                <p className="text-xs text-fg-subtle">
                  Chọn tập phim để nạp thẳng kịch bản và 5 phân cảnh shot vào Studio dựng video.
                </p>
              </div>
              <Button variant="ghost" size="sm" onClick={() => setShowDramaPicker(false)}>
                ✕ Đóng
              </Button>
            </div>

            <div className="space-y-3">
              {CHINESE_DRAMAS.map((drama) => (
                <div key={drama.id} className="rounded-lg border border-line p-3 space-y-2 bg-surface">
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <h5 className="font-semibold text-fg text-sm">{drama.title}</h5>
                      <p className="text-[11px] text-fg-subtle">{drama.vietnameseSetting}</p>
                    </div>
                    <Badge tone="success" className="text-[10px] shrink-0 font-medium">
                      Full Trọn Bộ ({drama.episodes.length} tập)
                    </Badge>
                  </div>
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 pt-1">
                    {drama.episodes.map((ep) => (
                      <button
                        key={ep.episodeNumber}
                        type="button"
                        onClick={() => applyDramaEpisode(drama.title, ep)}
                        className="rounded border border-line hover:border-brand bg-muted/40 hover:bg-brand/10 p-2 text-left transition-colors space-y-1 group"
                      >
                        <p className="font-semibold text-xs text-fg group-hover:text-brand">
                          Tập {ep.episodeNumber}
                        </p>
                        <p className="text-[11px] text-fg-subtle line-clamp-1">{ep.title.replace(/^Tập \d+:\s*/, '')}</p>
                        <span className="text-[10px] text-brand font-medium">⚡ Dùng tập này</span>
                      </button>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}
