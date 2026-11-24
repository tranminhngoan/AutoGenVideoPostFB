import { useEffect, useMemo, useState } from 'react';
import {
  ArrowDown, ArrowUp, Clapperboard, Download, Film, Mic, Plus, RefreshCw, Save, Send, Sparkles, Square, Trash2, Users, Volume2, Wand2,
} from 'lucide-react';
import { Badge, Button, Card, Field, Input, Select, Skeleton, Textarea, cn } from '../ui';
import { useConfirm, useToast } from '../feedback';
import {
  BUSY, STATUS_LABEL, StudioCharacter, StudioLocation, StudioMusic, StudioPage, StudioSettings, StudioShot, StudioShotInput, StudioVideo,
  StudioVideoInput, studio, usd,
  StudioScriptImport,
} from '../../services/studio';
import { navigate } from '../../lib/router';
import { ALL_NOSTALGIA_SCRIPTS, NOSTALGIA_CATEGORIES, NostalgiaScript } from '../../data/nostalgiaScriptLibrary';
import { IMPORT_LENGTHS, SPEEDS, estimateImport, narratorFor, takeStudioImport, toStudioImport } from '../../lib/studioImport';

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
  speaker: s.speaker ?? '', dialogue: s.dialogue ?? '', sound: s.sound ?? '', scene: s.scene ?? null, voiceStyle: s.voiceStyle ?? '',
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
    sfxEnabled: true, sfxVolume: 0.7, musicMode: 'Auto', musicTrackId: null, musicVolume: 0.15, facebookAccountId: null,
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
  /** A library script waiting to be turned into a video (shot for shot, no planning). */
  const [importing, setImporting] = useState<StudioScriptImport | null>(null);
  const [narratorVoice, setNarratorVoice] = useState('gemini:Charon');
  const [narratorSpeed, setNarratorSpeed] = useState('+0%');
  /** Seconds to shorten a library script to (0 = keep its length). */
  const [importLength, setImportLength] = useState(0);
  const loadImport = (s: StudioScriptImport) => {
    const n = narratorFor(s.mood);
    setImporting(s);
    setImportLength(estimateImport(s).seconds > 105 ? 90 : 0); // long scripts default to a ~1:30 video
    setNarratorVoice(n.voice);
    setNarratorSpeed(n.speed);
    setForm((prev) => ({ ...prev, title: s.code ? `${s.title} (${s.code})` : s.title, script: s.script ?? '' }));
  };
  const [pickerQuery, setPickerQuery] = useState('');
  const [pickerCategory, setPickerCategory] = useState<string>('all');

  const filteredPickerScripts = useMemo(() => {
    return ALL_NOSTALGIA_SCRIPTS.filter((s) => {
      if (pickerCategory !== 'all' && s.category !== pickerCategory) return false;
      if (pickerQuery.trim()) {
        const q = pickerQuery.toLowerCase().trim();
        return (
          s.title.toLowerCase().includes(q) ||
          s.code.toLowerCase().includes(q) ||
          s.hook.toLowerCase().includes(q)
        );
      }
      return true;
    });
  }, [pickerCategory, pickerQuery]);

  // Pick a library script from inside the editor: it is built shot for shot, like "Dựng video" on the script page
  const applyNostalgiaScript = (script: NostalgiaScript) => {
    loadImport(toStudioImport(script));
    setShowDramaPicker(false);
    toast.success(`Đã chọn kịch bản "${script.title}" — chọn Page, model, nhạc rồi bấm Tạo video.`);
  };

  // ── loading ──
  const applyVideo = (v: StudioVideo, resetShots = true) => {
    setVideo(v);
    setForm({
      title: v.title, script: v.script, targetDurationSeconds: v.targetDurationSeconds, videoModel: v.videoModel, aspectRatio: v.aspectRatio,
      resolution: v.resolution, styleNotes: v.styleNotes ?? '', sfxEnabled: v.sfxEnabled, sfxVolume: v.sfxVolume,
      musicMode: v.musicMode, musicTrackId: v.musicTrackId ?? null,
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

  // A script sent from the script page ("Dựng video")
  useEffect(() => {
    if (!isNew) return;
    const pending = takeStudioImport();
    if (pending) loadImport(pending);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isNew]);

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
  const sfxPrice = settings?.sfxPricePerSecondUsd ?? 0.001;
  const model = models.find((m) => m.id === form.videoModel) ?? models[0];
  const rateOf = (modelId?: string, res?: string) => {
    const m = models.find((x) => x.id === modelId) ?? model;
    return m?.pricePerSecondUsd[res ?? '720p'] ?? 0.02;
  };
  const perSecond = rateOf(form.videoModel, form.resolution);
  const shotCost = (x: { id?: string; durationSeconds: number; dialogue?: string | null }, rate = perSecond) => {
    const st = video?.shots.find((s) => s.id === x.id);
    return clampSecs(x.durationSeconds) * rate
      + (x.dialogue?.trim() && !st?.hasVoice && !st?.voiceFromShot ? secondsToSay(x.dialogue) * voicePrice : 0)
      + (form.sfxEnabled && !st?.hasSfx ? clampSecs(x.durationSeconds) * sfxPrice : 0);
  };
  const shotSeconds = shots.reduce((s, x) => s + clampSecs(x.durationSeconds), 0);
  const estimate = shots.reduce((s, x) => s + shotCost(x), 0);
  const pendingShots = (video?.shots ?? []).filter((s) => !s.hasClip || s.status !== 'Done');
  const pendingSeconds = pendingShots.reduce((s, x) => s + clampSecs(x.durationSeconds), 0);
  const pendingCost = pendingShots.reduce((s, x) => s + shotCost(x), 0);
  // a scene of a library script is read in one take: it counts once
  const missingVoices = new Set((video?.shots ?? []).filter((s) => s.dialogue?.trim() && !s.hasVoice && !s.voiceFromShot)
    .map((s) => (s.scene ? `scene-${s.scene}` : s.id))).size;
  /** The shot a scene's one-take narration starts on (its first shot with words). */
  const isTakeStart = (i: number) => {
    const s = shots[i];
    if (!s.scene || !s.dialogue?.trim()) return false;
    return !shots.slice(0, i).some((x) => x.scene === s.scene && x.dialogue?.trim());
  };
  const setSceneStyle = (scene: number, voiceStyle: string) =>
    setShots((list) => list.map((x) => (x.scene === scene ? { ...x, voiceStyle } : x)));
  const clipsWithoutSfx = (video?.shots ?? []).filter((s) => s.hasClip && !s.hasSfx);
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

  const importEstimate = useMemo(() => (importing ? estimateImport(importing, importLength || null) : null), [importing, importLength]);
  const fullImportSeconds = useMemo(() => (importing ? estimateImport(importing).seconds : 0), [importing]);
  const importCost = importEstimate
    ? importEstimate.seconds * perSecond + importEstimate.seconds * voicePrice * 0.8 + (form.sfxEnabled ? importEstimate.seconds * sfxPrice : 0)
    : 0;

  const createFromScript = (autoGenerate: boolean) =>
    guarded(autoGenerate ? 'import-go' : 'import', async () => {
      if (!importing) return;
      if (autoGenerate) {
        const label = models.find((m) => m.id === form.videoModel)?.label ?? form.videoModel;
        if (!(await confirm({
          title: `Tạo video "${importing.title}" ngay?`,
          description: `Dựng theo kịch bản${importLength ? ` (lời bình rút gọn còn ~${fmtDuration(importLength)})` : ''}: ${importing.scenes.length} phân cảnh → khoảng ${importEstimate?.shots} clip, ~${fmtDuration(importEstimate?.seconds ?? 0)} bằng ${label} ${form.resolution}. Ước tính ~${usd(importCost)} (video + giọng đọc${form.sfxEnabled ? ' + hiệu ứng âm thanh' : ''}). Lời bình đọc bằng giọng ${voiceName(narratorVoice)}${form.musicMode !== 'None' ? ', có nhạc nền' : ''}. Mỗi clip mất vài phút; trang tự cập nhật.`,
          confirmLabel: `Tạo video (~${usd(importCost)})`,
        }))) return;
      }
      const v = await studio.importScript({
        ...importing,
        title: importing.title,
        facebookAccountId: form.facebookAccountId,
        videoModel: form.videoModel,
        resolution: form.resolution,
        aspectRatio: form.aspectRatio,
        styleNotes: form.styleNotes?.trim() || undefined,
        narratorVoice,
        narratorSpeed,
        musicMode: form.musicMode,
        musicTrackId: form.musicTrackId,
        musicVolume: form.musicVolume,
        sfxEnabled: form.sfxEnabled,
        sfxVolume: form.sfxVolume,
        autoGenerate,
        targetSeconds: importLength || null,
      });
      toast.success(autoGenerate
        ? 'Đã nhận kịch bản — đang chuẩn bị rồi tạo video, trang tự cập nhật.'
        : 'Đã dựng các shot đúng theo kịch bản — xem, nghe thử giọng rồi bấm Tạo video.');
      navigate(`/studio/videos/${v.id}`);
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

  /** A library-script video with no clip paid for yet can still be shortened. */
  const canCondense = !!video && video.shots.length > 0 && video.shots.every((s) => s.scene && !s.hasClip) && shotSeconds > 70;
  const condense = (target: number) =>
    guarded('condense-' + target, async () => {
      if (!(await confirm({
        title: `Rút gọn video còn ~${fmtDuration(target)}?`,
        description: 'AI cắt bớt lời bình rồi dựng lại các shot, hồ sơ nhân vật và mô tả cảnh (mất 1–2 phút, vài cent). Các chỉnh sửa tay ở shot hiện tại sẽ bị thay.',
        confirmLabel: 'Rút gọn',
      }))) return;
      applyVideo(await studio.condense(videoId!, target), true);
      toast.success('Đang rút gọn — trang tự cập nhật khi xong.');
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

  const addEffects = () =>
    guarded('effects', async () => {
      const cost = clipsWithoutSfx.reduce((s, x) => s + clampSecs(x.durationSeconds) * sfxPrice, 0);
      if (!(await confirm({
        title: `Thêm hiệu ứng âm thanh cho ${clipsWithoutSfx.length} clip?`,
        description: `AI tạo tiếng động theo mô tả âm thanh của từng shot (mưa, gió, bếp củi, chim, ếch nhái…), dài đúng bằng clip, khoảng ${usd(cost, 3)}. Xong thì video tự ghép lại, nhạc nền vẫn giữ.`,
        confirmLabel: `Thêm (~${usd(cost, 3)})`,
      }))) return;
      applyVideo(await studio.effects(videoId!), true);
    });

  const redoEffects = (s: StudioShotInput) =>
    guarded('fx-' + s.id, async () => {
      if (shotsDirty) applyVideo(await studio.saveShots(videoId!, shots), true);
      applyVideo(await studio.shotEffects(videoId!, s.id!), true);
    });

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
          <Button variant="secondary" size="sm" icon={<Sparkles className="h-3.5 w-3.5 text-fg-muted" />} onClick={() => setShowDramaPicker(true)}
            className="text-xs font-medium">
            📜 Kịch bản ký ức tuổi thơ (300)
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
        {isNew && importing ? (
          <div className="space-y-3 rounded-lg border border-accent/30 bg-accent/[0.04] p-3">
            <div className="flex flex-wrap items-start gap-2">
              <div className="min-w-0 flex-1">
                <div className="text-sm font-semibold text-fg">🎬 Dựng đúng theo kịch bản: {importing.title}{importing.code ? ` (${importing.code})` : ''}</div>
                <div className="mt-0.5 text-xs text-fg-muted">
                  {[importing.mood, importing.region, importing.era].filter(Boolean).join(' · ')} · {importing.scenes.length} phân cảnh → khoảng{' '}
                  <b className="text-fg">{importEstimate?.shots} clip, ~{fmtDuration(importEstimate?.seconds ?? 0)}</b>
                  {(importEstimate?.seconds ?? 0) > REELS_MAX_SECONDS ? ' (quá 90s → đăng thành video thường, không vào Reels)' : ' (≤ 90s → Reels)'}
                </div>
              </div>
              <Button size="sm" variant="ghost" onClick={() => setImporting(null)} disabled={locked}>Bỏ kịch bản này</Button>
            </div>
            {importing.hook && <p className="text-xs text-fg"><b>Hook mở đầu:</b> {importing.hook}</p>}
            <ol className="space-y-1 text-xs text-fg-muted">
              {importing.scenes.map((sc, i) => (
                <li key={i}>
                  <b className="text-fg">Cảnh {i + 1}</b> ({sc.durationSeconds}s) — {sc.title}
                  {sc.sound ? <span className="text-fg-subtle"> · 🔊 {sc.sound}</span> : null}
                </li>
              ))}
            </ol>
            <p className="text-[11px] text-fg-subtle">
              Mỗi phân cảnh dùng đúng prompt tiếng Anh của kịch bản, chia thành các clip ≤ 12s với góc máy khác nhau; lời bình (kèm hook và lời kết)
              đọc bằng giọng AI theo cảm xúc; mô tả âm thanh của kịch bản được dịch sang tiếng Anh để AI tạo đúng tiếng động đó; nhạc nền AI chọn hợp tâm trạng.
            </p>
            <Field label={`Độ dài video (kịch bản gốc khoảng ${fmtDuration(fullImportSeconds)})`}
              hint="Rút gọn: AI cắt bớt câu trùng lặp, giữ câu mở đầu, câu kết và những chi tiết cảm động nhất — rẻ hơn và hợp Reels hơn.">
              <Select value={importLength} onChange={(e) => setImportLength(Number(e.target.value))} disabled={locked}>
                {IMPORT_LENGTHS.filter((l) => l.id === 0 || l.id < fullImportSeconds).map((l) => (
                  <option key={l.id} value={l.id}>{l.label}</option>
                ))}
              </Select>
            </Field>
            <div className="grid gap-2 sm:grid-cols-[1fr_180px]">
              <Field label="Giọng lời bình (chọn theo cảm xúc kịch bản, đổi được)">
                <div className="flex gap-1">
                  <Select value={narratorVoice} onChange={(e) => setNarratorVoice(e.target.value)} disabled={locked}>
                    {voices.map((v) => (
                      <option key={v.id} value={v.id}>{v.name} — {v.description}</option>
                    ))}
                  </Select>
                  <Button size="sm" variant="ghost" icon={audio.playing === 'narrator' ? <Square className="h-3.5 w-3.5" /> : <Volume2 className="h-3.5 w-3.5" />}
                    onClick={() => audio.toggle('narrator', studio.voicePreviewUrl(narratorVoice))} aria-label="Nghe thử giọng" title="Nghe thử giọng" />
                </div>
              </Field>
              <Field label="Tốc độ đọc">
                <Select value={narratorSpeed} onChange={(e) => setNarratorSpeed(e.target.value)} disabled={locked}>
                  {SPEEDS.map((sp) => (
                    <option key={sp.id} value={sp.id}>{sp.label}</option>
                  ))}
                </Select>
              </Field>
            </div>
          </div>
        ) : (
          <Field label="Kịch bản" hint="Mỗi câu thoại 1 dòng dạng “Tên: lời thoại”, hành động / bối cảnh ở dòng riêng. Video dài đúng theo kịch bản — AI không thêm, không cắt. Lời thoại được đọc bằng giọng AI.">
            <Textarea rows={10} value={form.script} onChange={(e) => setForm({ ...form, script: e.target.value })} disabled={locked} />
          </Field>
        )}
        {!(isNew && importing) && form.script?.trim() && (
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
          <Field label="Hiệu ứng âm thanh AI" hint={settings && !settings.sfxConfigured ? 'Chưa có khoá Vercel AI Gateway (AI_GATEWAY_API_KEY) — thêm khoá để bật.' : `Tiếng mưa, gió, bếp củi, ếch nhái… theo mô tả âm thanh của từng shot · ~${usd(sfxPrice, 3)}/giây`}>
            <Select value={form.sfxEnabled ? 'on' : 'off'} onChange={(e) => setForm({ ...form, sfxEnabled: e.target.value === 'on' })} disabled={locked}>
              <option value="on">Có — AI tạo tiếng động theo mô tả âm thanh của từng shot</option>
              <option value="off">Không</option>
            </Select>
          </Field>
          {form.sfxEnabled && (
            <Field label={`Âm lượng hiệu ứng: ${Math.round((form.sfxVolume ?? 0.7) * 100)}%`} hint="Giọng nói luôn rõ hơn; nhạc nền không bị hiệu ứng làm nhỏ đi.">
              <input type="range" min={0} max={1.5} step={0.05} value={form.sfxVolume ?? 0.7}
                onChange={(e) => setForm({ ...form, sfxVolume: Number(e.target.value) })} className="w-full" disabled={locked} />
            </Field>
          )}
        </div>
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
        {isNew && importing ? (
          <div className="flex flex-wrap items-center gap-2">
            <Button variant="primary" icon={<Sparkles className="h-4 w-4" />} loading={busy === 'import-go'} disabled={locked || !settings?.videoConfigured} onClick={() => createFromScript(true)}>
              Tạo video ngay (~{usd(importCost)})
            </Button>
            <Button variant="secondary" icon={<Save className="h-4 w-4" />} loading={busy === 'import'} disabled={locked} onClick={() => createFromScript(false)}>
              Dựng shot để xem / sửa trước
            </Button>
            <span className="text-xs text-fg-muted">
              Không cần lập kế hoạch: app dựng đúng kịch bản. "Xem / sửa trước" cho phép nghe giọng, sửa shot rồi mới bấm tạo.
              {settings && !settings.videoConfigured && ' Chưa có khoá Vercel AI Gateway nên chưa tạo video được.'}
            </span>
          </div>
        ) : (
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
        )}
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
                  <div key={i} className="grid gap-2 rounded-lg border border-line p-2.5 sm:grid-cols-[150px_100px_1fr_220px_130px]">
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
                    <Field label="Tốc độ đọc">
                      <Select value={c.speed ?? '+0%'} disabled={locked} onChange={(e) => updateCharacter(i, { speed: e.target.value })}>
                        {SPEEDS.map((sp) => (
                          <option key={sp.id} value={sp.id}>{sp.label}</option>
                        ))}
                      </Select>
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
            {canCondense && (
              <div className="flex flex-wrap items-center gap-2 text-xs text-fg-muted">
                <span>Video dài quá? AI rút gọn lời bình (giữ mở đầu, kết và chi tiết cảm động), dựng lại shot:</span>
                {[90, 60].filter((t) => shotSeconds > t + 10).map((t) => (
                  <Button key={t} size="sm" variant="secondary" loading={busy === 'condense-' + t} disabled={locked} onClick={() => condense(t)}>
                    Rút gọn còn ~{fmtDuration(t)}
                  </Button>
                ))}
              </div>
            )}
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
                        {s.scene ? <Badge tone="neutral">Cảnh {s.scene}</Badge> : null}
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
                          {needs > clampSecs(s.durationSeconds) && !st?.hasVoice && !s.scene && (
                            <p className="text-[11px] text-warning">
                              Câu thoại cần khoảng {Math.min(12, needs)}s — shot sẽ tự dài ra cho vừa giọng đọc.
                              {needs > 12 && ' Câu quá dài cho 1 clip (tối đa 12s): nên tách thành 2 shot.'}
                            </p>
                          )}
                          {st?.hasVoice && s.id && (
                            <div className="flex flex-wrap items-center gap-2 text-[11px] text-fg-muted">
                              <Button size="sm" variant="ghost" icon={audio.playing === 'line-' + s.id ? <Square className="h-3.5 w-3.5" /> : <Volume2 className="h-3.5 w-3.5" />}
                                onClick={() => audio.toggle('line-' + s.id, studio.voiceUrl(s.id!) + `?v=${st.voiceSeconds}`)}>
                                {s.scene ? `Nghe lời bình cả cảnh ${s.scene}` : 'Nghe giọng đọc'} ({st.voiceSeconds?.toFixed(1)}s)
                              </Button>
                              <span>giọng {voiceName(characters.find((c) => c.name.trim().toLowerCase() === (s.speaker ?? '').trim().toLowerCase())?.voice ?? 'gemini:Charon')}</span>
                              <Button size="sm" variant="ghost" icon={<Mic className="h-3.5 w-3.5" />} loading={busy === 'voice-' + s.id} disabled={locked} onClick={() => rereadShot(s)}>
                                {s.scene ? 'Đọc lại cả cảnh' : 'Đọc lại'}
                              </Button>
                            </div>
                          )}
                          {st?.voiceFromShot && (
                            <p className="text-[11px] text-fg-muted">
                              🎙 Câu này nằm trong lời bình cả cảnh {s.scene}, đọc liền một mạch từ shot {st.voiceFromShot} — nghe ở shot đó.
                            </p>
                          )}
                          {isTakeStart(i) && (
                            <Field label={`Cách đọc lời bình cảnh ${s.scene} (tiếng Anh: cảm xúc, nhịp — AI diễn theo, không đọc ra)`}>
                              <Input value={s.voiceStyle ?? ''} disabled={locked} placeholder="tender, slow, voice catching at the saddest words…"
                                onChange={(e) => setSceneStyle(s.scene!, e.target.value)} />
                            </Field>
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
                            <Field label="Hiệu ứng âm thanh (tiếng Anh — AI tạo tiếng theo mô tả này)">
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
                          {st?.hasClip && s.id && form.sfxEnabled && (
                            <div className="mt-1 flex flex-wrap gap-1">
                              {st.hasSfx && (
                                <Button size="sm" variant="ghost" icon={audio.playing === 'fx-' + s.id ? <Square className="h-3.5 w-3.5" /> : <Volume2 className="h-3.5 w-3.5" />}
                                  onClick={() => audio.toggle('fx-' + s.id, studio.effectsUrl(s.id!) + `?v=${st.costUsd}`)}>
                                  Nghe hiệu ứng
                                </Button>
                              )}
                              <Button size="sm" variant="ghost" icon={<Wand2 className="h-3.5 w-3.5" />} loading={busy === 'fx-' + s.id}
                                disabled={locked || !settings?.sfxConfigured} onClick={() => redoEffects(s)}>
                                {st.hasSfx ? 'Tạo lại hiệu ứng' : 'Tạo hiệu ứng'}
                              </Button>
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
                ở <b>{video.resolution}</b> ({usd(rateOf(video.videoModel, video.resolution), 4)}/giây). Video gốc không có tiếng: lời thoại được đọc bằng giọng AI,
                {form.sfxEnabled ? ' AI tạo thêm tiếng động theo mô tả âm thanh của từng shot (mưa, gió, bếp củi…),' : ''} rồi trộn với nhạc nền, ghép liền và xuất 1080p. Còn {pendingSeconds}s chưa tạo, ước tính <b>{usd(pendingCost)}</b>
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
              {form.sfxEnabled && settings?.sfxConfigured && clipsWithoutSfx.length > 0 && !serverBusy && (
                <Button variant="secondary" icon={<Volume2 className="h-4 w-4" />} loading={busy === 'effects'} disabled={locked} onClick={addEffects}>
                  Thêm hiệu ứng âm thanh cho {clipsWithoutSfx.length} clip
                </Button>
              )}
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

      {/* Childhood Nostalgia Scripts Picker Modal */}
      {showDramaPicker && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-0 sm:p-4 backdrop-blur-sm">
          <Card className="h-full sm:h-auto sm:max-h-[85vh] w-full max-w-2xl flex flex-col p-4 sm:p-5 space-y-3.5 shadow-2xl border-line bg-surface sm:rounded-2xl overflow-hidden">
            <div className="flex items-start justify-between border-b border-line pb-3">
              <div>
                <h4 className="font-bold text-fg text-sm sm:text-base flex items-center gap-2">
                  <Sparkles className="h-4 w-4 text-fg-muted" /> Thư viện kịch bản ký ức tuổi thơ (300)
                </h4>
                <p className="text-xs text-fg-subtle">
                  Chọn kịch bản để nạp toàn bộ 5 phân cảnh chuẩn AI và lời dẫn vào Studio.
                </p>
              </div>
              <Button variant="ghost" size="sm" onClick={() => setShowDramaPicker(false)} className="text-xs">
                ✕ Đóng
              </Button>
            </div>

            {/* Filter and Search inside picker */}
            <div className="flex flex-col sm:flex-row gap-2">
              <input
                type="text"
                placeholder="Tìm kịch bản: cơm độn, kem mút, diều, mẹ vá áo..."
                value={pickerQuery}
                onChange={(e) => setPickerQuery(e.target.value)}
                className="flex-1 rounded-lg border border-line bg-canvas px-3 py-2 text-xs text-fg focus:outline-none focus:border-fg/40"
              />
              <select
                value={pickerCategory}
                onChange={(e) => setPickerCategory(e.target.value)}
                className="rounded-lg border border-line bg-canvas px-2.5 py-2 text-xs text-fg focus:outline-none"
              >
                <option value="all">Tất cả 6 chủ đề ({ALL_NOSTALGIA_SCRIPTS.length})</option>
                {NOSTALGIA_CATEGORIES.map((cat) => (
                  <option key={cat.id} value={cat.id}>
                    {cat.icon} {cat.name} (50)
                  </option>
                ))}
              </select>
            </div>

            {/* Script List */}
            <div className="space-y-2 flex-1 overflow-y-auto pr-1">
              {filteredPickerScripts.slice(0, 50).map((script) => (
                <div
                  key={script.id}
                  className="rounded-lg border border-line p-3 hover:border-line-strong hover:bg-subtle transition-all flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2.5 group"
                >
                  <div className="space-y-1 pr-2">
                    <div className="flex items-center gap-1.5 text-xs">
                      <span className="font-mono text-[10px] font-medium text-fg-muted bg-muted px-1.5 py-0.5 rounded">
                        {script.code}
                      </span>
                      <span className="text-[10px] text-fg-subtle">
                        {script.region} • {script.era}
                      </span>
                      <span className="text-[10px] text-fg-muted">
                        {script.mood}
                      </span>
                    </div>
                    <h5 className="font-semibold text-fg text-xs group-hover:text-fg transition-colors">
                      {script.title}
                    </h5>
                    <p className="text-[11px] text-fg-subtle line-clamp-1 italic">
                      "{script.hook}"
                    </p>
                  </div>

                  <Button
                    variant="primary"
                    size="sm"
                    onClick={() => applyNostalgiaScript(script)}
                    className="shrink-0 text-xs w-full sm:w-auto font-medium"
                  >
                    Dùng kịch bản
                  </Button>
                </div>
              ))}
              {filteredPickerScripts.length > 50 && (
                <p className="text-center text-xs text-fg-subtle py-2">
                  Đang hiển thị 50 / {filteredPickerScripts.length} kết quả. Dùng ô tìm kiếm để lọc nhanh hơn.
                </p>
              )}
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}
