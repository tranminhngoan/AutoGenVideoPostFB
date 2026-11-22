import { useEffect, useMemo, useState } from 'react';
import {
  ArrowDown, ArrowUp, Clapperboard, Download, Film, Plus, RefreshCw, Save, Send, Sparkles, Trash2, Wand2,
} from 'lucide-react';
import { Badge, Button, Card, Checkbox, Field, Input, Select, Skeleton, Textarea, cn } from '../ui';
import { useConfirm, useToast } from '../feedback';
import {
  BUSY, STATUS_LABEL, StudioAsset, StudioMusic, StudioPage, StudioShot, StudioShotInput, StudioVideo, StudioVideoInput, studio, usd,
} from '../../services/studio';
import { navigate } from '../../lib/router';
import { AssetPicker } from './AssetLibrary';

/** Facebook only takes Reels up to 90 s; longer videos go up as ordinary Page videos. */
const REELS_MAX_SECONDS = 90;

/**
 * Rough length of a script: a "Name: line" is speech (~2.5 Vietnamese words a second + a breath), any other line is an
 * action/scene beat (~3 s). Only a guide for the writer — the real length is the sum of the planned shots.
 */
export function estimateScriptSeconds(script: string): { seconds: number; lines: number; actions: number } {
  let seconds = 0;
  let lines = 0;
  let actions = 0;
  for (const raw of script.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line) continue;
    const speech = /^([^:：]{1,40})[:：]\s*(.+)$/.exec(line);
    if (speech) {
      const words = speech[2].replace(/\([^)]*\)/g, ' ').split(/\s+/).filter(Boolean).length;
      seconds += words / 2.5 + 1;
      lines++;
    } else {
      seconds += 3;
      actions++;
    }
  }
  return { seconds: Math.round(seconds), lines, actions };
}

export const fmtDuration = (s: number) => (s >= 60 ? `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')}` : `${Math.round(s)}s`);
const RESOLUTIONS: { id: string; label: string }[] = [
  { id: '720p', label: '720p (khuyên dùng)' },
  { id: '480p', label: '480p (nháp, rẻ nhất)' },
  { id: '1080p', label: '1080p (model tự upscale từ 720p, đắt gấp ~5)' },
];
/** xAI bills each input image; a clip starting from a picture (asset or previous clip's last frame) pays it once. */
const IMAGE_FEE = 0.01;
const clampSecs = (n: number) => Math.min(15, Math.max(1, n || 0));
const usesFrame = (s: { assetIds: string[]; continueFromPrevious?: boolean }) => !!s.continueFromPrevious || s.assetIds.length > 0;

function statusTone(s: string): 'success' | 'warning' | 'danger' | 'info' | 'neutral' {
  if (s === 'Published' || s === 'Ready' || s === 'Done') return 'success';
  if (s === 'Failed') return 'danger';
  if (BUSY.includes(s as any) || s === 'Generating') return 'warning';
  if (s === 'Planned') return 'info';
  return 'neutral';
}

const toInput = (s: StudioShot): StudioShotInput => ({
  id: s.id, durationSeconds: s.durationSeconds, action: s.action, camera: s.camera, speaker: s.speaker ?? '', dialogue: s.dialogue ?? '',
  sound: s.sound ?? '', assetIds: s.assetIds, continueFromPrevious: s.continueFromPrevious,
});

/** Create or work on one video: details → shot plan → clips → final video → Facebook. */
export function VideoEditor({ videoId, onAuthError }: { videoId?: string; onAuthError: (e: unknown) => boolean }) {
  const toast = useToast();
  const confirm = useConfirm();
  const isNew = !videoId;

  const [video, setVideo] = useState<StudioVideo | null>(null);
  const [form, setForm] = useState<StudioVideoInput>({
    title: '', script: '', targetDurationSeconds: 30, aspectRatio: '9:16', resolution: '720p', styleNotes: '', assetIds: [],
    musicMode: 'Auto', musicTrackId: null, musicVolume: 0.15, facebookAccountId: null,
  });
  const [shots, setShots] = useState<StudioShotInput[]>([]);
  const [shotsDirty, setShotsDirty] = useState(false);
  const [caption, setCaption] = useState('');
  const [pages, setPages] = useState<StudioPage[]>([]);
  const [assets, setAssets] = useState<StudioAsset[]>([]);
  const [music, setMusic] = useState<StudioMusic[]>([]);
  const [price, setPrice] = useState<Record<string, number>>({});
  const [remaining, setRemaining] = useState<number | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  // ── loading ──
  const applyVideo = (v: StudioVideo, resetShots = true) => {
    setVideo(v);
    setForm({
      title: v.title, script: v.script, targetDurationSeconds: v.targetDurationSeconds, aspectRatio: v.aspectRatio, resolution: v.resolution,
      styleNotes: v.styleNotes ?? '', assetIds: v.assetIds, musicMode: v.musicMode, musicTrackId: v.musicTrackId ?? null,
      musicVolume: v.musicVolume, facebookAccountId: v.facebookAccountId ?? null,
    });
    setCaption(v.caption ?? '');
    if (resetShots) {
      setShots(v.shots.map(toInput));
      setShotsDirty(false);
    }
  };

  const refreshVideo = async (resetShots = !shotsDirty) => {
    if (!videoId) return;
    try {
      const v = await studio.video(videoId);
      if (resetShots) applyVideo(v, true);
      else setVideo(v);
    } catch (e) {
      toast.error((e as Error).message);
    }
  };

  useEffect(() => {
    (async () => {
      try {
        const [p, a, m, c] = await Promise.all([studio.pages(), studio.assets(), studio.music(), studio.costs()]);
        setPages(p.filter((x) => x.isActive));
        setAssets(a);
        setMusic(m);
        setPrice(c.pricePerSecondUsd);
        setRemaining(c.monthlyBudgetUsd > 0 ? c.monthRemainingUsd : null);
      } catch (e) {
        toast.error((e as Error).message);
      }
      if (videoId) refreshVideo(true);
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [videoId]);

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
      if (video.status === 'Ready') toast.success('Video đã ghép xong — xem thử bên dưới.');
      if (video.status === 'Published') toast.success('Đã đăng lên Facebook!');
      if (video.status === 'Failed' && video.errorMessage) toast.error(video.errorMessage);
    }
    setLastStatus(video?.status ?? null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [video?.status]);

  const perSecond = price[form.resolution ?? '720p'] ?? 0.03;
  const shotCost = (x: { durationSeconds: number; assetIds: string[]; continueFromPrevious?: boolean }, rate = perSecond) =>
    clampSecs(x.durationSeconds) * rate + (usesFrame(x) ? IMAGE_FEE : 0);
  const shotSeconds = shots.reduce((s, x) => s + clampSecs(x.durationSeconds), 0);
  const estimate = shots.reduce((s, x) => s + shotCost(x), 0);
  const pendingShots = (video?.shots ?? []).filter((s) => !s.hasClip || s.status !== 'Done');
  const pendingSeconds = pendingShots.reduce((s, x) => s + clampSecs(x.durationSeconds), 0);
  const pendingCost = pendingShots.reduce((s, x) => s + shotCost(x), 0);
  const assetById = useMemo(() => new Map(assets.map((a) => [a.id, a])), [assets]);
  const scriptEstimate = useMemo(() => estimateScriptSeconds(form.script ?? ''), [form.script]);

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

  const saveDetails = () =>
    guarded('save', async () => {
      if (isNew) {
        const v = await studio.createVideo({ ...form, targetDurationSeconds: Math.max(1, scriptEstimate.seconds) });
        toast.success('Đã tạo video nháp');
        navigate(`/studio/videos/${v.id}`);
      } else {
        applyVideo(await studio.updateVideo(videoId!, { ...form, caption, targetDurationSeconds: Math.max(1, scriptEstimate.seconds) }), !shotsDirty);
        toast.success('Đã lưu');
      }
    });

  const plan = () =>
    guarded('plan', async () => {
      if (!(await confirm({
        title: 'Lập kế hoạch shot bằng AI?',
        description: 'Gemini đọc kịch bản, ảnh tham chiếu và kho nhạc để chia thành các shot (chi phí vài phần trăm cent). Kế hoạch hiện tại (nếu có) sẽ được thay.',
        confirmLabel: 'Lập kế hoạch',
      }))) return;
      await studio.updateVideo(videoId!, { ...form, caption, targetDurationSeconds: Math.max(1, scriptEstimate.seconds) });
      applyVideo(await studio.plan(videoId!), true);
      toast.success('Đã có kế hoạch — kiểm tra và sửa nếu cần, rồi bấm Tạo video.');
    });

  const saveShots = () =>
    guarded('shots', async () => {
      applyVideo(await studio.saveShots(videoId!, shots), true);
      toast.success('Đã lưu kế hoạch shot');
    });

  const generate = () =>
    guarded('generate', async () => {
      if (shotsDirty) applyVideo(await studio.saveShots(videoId!, shots), true);
      const fresh = await studio.video(videoId!);
      const todo = fresh.shots.filter((s) => !s.hasClip || s.status !== 'Done');
      const secs = todo.reduce((s, x) => s + clampSecs(x.durationSeconds), 0);
      const cost = todo.reduce((s, x) => s + shotCost(x, price[fresh.resolution] ?? perSecond), 0);
      if (!(await confirm({
        title: `Tạo ${todo.length} clip bằng Grok Imagine?`,
        description: `Khoảng ${secs} giây video ở ${fresh.resolution}, ước tính ${usd(cost)} (xAI tính theo giây). Các clip được tạo lần lượt — shot "nối tiếp" lấy khung cuối của clip trước. Clip đã có sẽ không tạo lại. Mỗi clip mất vài phút; trang tự cập nhật.`,
        confirmLabel: `Tạo (~${usd(cost)})`,
      }))) return;
      applyVideo(await studio.generate(videoId!), true);
    });

  const regenerateShot = (s: StudioShotInput) =>
    guarded('shot-' + s.id, async () => {
      if (shotsDirty) applyVideo(await studio.saveShots(videoId!, shots), true);
      const cost = shotCost(s);
      const next = shots[shots.findIndex((x) => x.id === s.id) + 1];
      const warn = next?.continueFromPrevious ? ' Shot kế tiếp đang nối tiếp từ clip này — nên tạo lại cả shot đó để khớp hình.' : '';
      if (!(await confirm({ title: 'Tạo lại clip này?', description: `Khoảng ${usd(cost)}. Clip cũ bị thay; sau đó video được ghép lại.${warn}`, confirmLabel: 'Tạo lại' }))) return;
      applyVideo(await studio.generateShot(videoId!, s.id!), true);
    });

  const compose = () => guarded('compose', async () => applyVideo(await studio.compose(videoId!), true));

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
      if (!(await confirm({ title: 'Xoá các clip lẻ?', description: 'Giữ video hoàn chỉnh, xoá các clip từng shot để giải phóng database. Sau đó không tạo lại từng shot được nữa.', confirmLabel: 'Xoá clip' }))) return;
      applyVideo(await studio.purgeClips(videoId!), true);
    });

  const remove = () =>
    guarded('delete', async () => {
      if (!(await confirm({ title: 'Xoá video này?', description: 'Xoá kế hoạch, mọi clip và video hoàn chỉnh. Không khôi phục được (chi phí đã chi vẫn được ghi lại).', confirmLabel: 'Xoá', danger: true }))) return;
      await studio.deleteVideo(videoId!);
      navigate('/studio');
    });

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
    setShots((list) => [...list, { durationSeconds: 8, action: '', camera: 'medium shot, static', speaker: '', dialogue: '', sound: '', assetIds: [], continueFromPrevious: false }]);
    setShotsDirty(true);
  };
  const removeShot = (i: number) => {
    setShots((list) => list.filter((_, k) => k !== i));
    setShotsDirty(true);
  };

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
        <div className="flex items-center gap-2 text-sm font-semibold text-fg">
          <Clapperboard className="h-4 w-4" /> 1. Thông tin video
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
            <Input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} placeholder="vd: Anh Hai mời Út Tèo nhậu" disabled={locked} />
          </Field>
        </div>
        <Field label="Kịch bản" hint="Dán nguyên kịch bản, mỗi câu thoại 1 dòng dạng “Tên: lời thoại”, hành động / bối cảnh ở dòng riêng. Video dài đúng theo kịch bản — AI không thêm, không cắt.">
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
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Khung hình">
            <Select value={form.aspectRatio} onChange={(e) => setForm({ ...form, aspectRatio: e.target.value })} disabled={locked}>
              <option value="9:16">Dọc 9:16 (Reels)</option>
              <option value="16:9">Ngang 16:9</option>
            </Select>
          </Field>
          <Field label="Độ phân giải clip" hint={`${usd(perSecond, 3)}/giây · video cuối luôn xuất 1080p`}>
            <Select value={form.resolution} onChange={(e) => setForm({ ...form, resolution: e.target.value })} disabled={locked}>
              {RESOLUTIONS.map((r) => (
                <option key={r.id} value={r.id}>{r.label}</option>
              ))}
            </Select>
          </Field>
        </div>
        <Field label="Phong cách / ghi chú cho đạo diễn AI" optional hint="vd: giọng miền Tây, nắng chiều vàng, hài hước, máy quay cầm tay nhẹ">
          <Input value={form.styleNotes} onChange={(e) => setForm({ ...form, styleNotes: e.target.value })} disabled={locked} />
        </Field>
        <Field label="Ảnh nhân vật & bối cảnh dùng trong video" hint="Chọn những ảnh trong kho sẽ xuất hiện; AI chọn ảnh mở đầu cho từng shot. Ảnh có sẵn nhân vật đứng trong bối cảnh cho kết quả giống mặt nhất.">
          <AssetPicker assets={assets} value={form.assetIds ?? []} onChange={(ids) => setForm({ ...form, assetIds: ids })} />
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
            {isNew ? 'Tạo video nháp' : 'Lưu thông tin'}
          </Button>
          {!isNew && (
            <Button variant="danger-soft" icon={<Trash2 className="h-4 w-4" />} loading={busy === 'delete'} disabled={locked} onClick={remove}>
              Xoá video
            </Button>
          )}
        </div>
      </Card>

      {!isNew && video && (
        <>
          {/* 2. Shot plan */}
          <Card className="space-y-4 p-4">
            <div className="flex flex-wrap items-center gap-2">
              <div className="flex items-center gap-2 text-sm font-semibold text-fg">
                <Wand2 className="h-4 w-4" /> 2. Kế hoạch shot
              </div>
              <span className="text-xs text-fg-muted">
                {shots.length} shot · dài {fmtDuration(shotSeconds)}{shotSeconds > REELS_MAX_SECONDS ? ' (quá 90s → video thường, không phải Reels)' : ''} · ước tính {usd(estimate)}
              </span>
              <Button className="ml-auto" size="sm" variant="secondary" icon={<Sparkles className="h-4 w-4" />} loading={busy === 'plan'}
                disabled={locked || !form.script?.trim() || video.shots.some((s) => s.hasClip)} onClick={plan}
                title={video.shots.some((s) => s.hasClip) ? 'Đã có clip — tạo video mới nếu muốn lập kế hoạch lại' : ''}>
                {shots.length ? 'Lập lại kế hoạch bằng AI' : 'Lập kế hoạch bằng AI'}
              </Button>
            </div>
            {video.planNotes && <p className="rounded-lg bg-subtle p-2.5 text-xs text-fg-muted">🎬 {video.planNotes}</p>}
            {shots.length === 0 ? (
              <p className="text-xs text-fg-muted">Chưa có shot. Bấm "Lập kế hoạch bằng AI" để Gemini chia kịch bản (hoặc thêm tay).</p>
            ) : (
              <ol className="space-y-3">
                {shots.map((s, i) => {
                  const st = shotState(s.id);
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
                            <Field label="Giây (1–15)">
                              <Input type="number" min={1} max={15} value={s.durationSeconds} disabled={locked}
                                onChange={(e) => updateShot(i, { durationSeconds: Number(e.target.value) || 8 })} />
                            </Field>
                            <Field label="Người nói">
                              <Input value={s.speaker ?? ''} disabled={locked} onChange={(e) => updateShot(i, { speaker: e.target.value })} placeholder="(không thoại)" />
                            </Field>
                            <Field label="Lời thoại (tiếng Việt, nguyên văn)">
                              <Input value={s.dialogue ?? ''} disabled={locked} onChange={(e) => updateShot(i, { dialogue: e.target.value })} />
                            </Field>
                          </div>
                          <Field label="Hành động (tiếng Anh để model hiểu tốt nhất)">
                            <Textarea rows={2} value={s.action} disabled={locked} onChange={(e) => updateShot(i, { action: e.target.value })} />
                          </Field>
                          <div className="grid gap-2 sm:grid-cols-2">
                            <Field label="Góc máy">
                              <Input value={s.camera} disabled={locked} onChange={(e) => updateShot(i, { camera: e.target.value })} />
                            </Field>
                            <Field label="Âm thanh môi trường">
                              <Input value={s.sound ?? ''} disabled={locked} onChange={(e) => updateShot(i, { sound: e.target.value })} />
                            </Field>
                          </div>
                          {i > 0 && (
                            <Checkbox checked={!!s.continueFromPrevious} disabled={locked} onChange={(c) => updateShot(i, { continueFromPrevious: c })}
                              label="Nối tiếp từ khung cuối của shot trước"
                              description="Cùng cảnh, cùng người: clip bắt đầu đúng chỗ clip trước dừng nên mặt nhân vật liền mạch. Bỏ chọn khi đổi góc máy / đổi cảnh." />
                          )}
                          <Field label={s.continueFromPrevious && i > 0 ? 'Ảnh mô tả (không dùng làm khung mở đầu vì đang nối tiếp)' : 'Ảnh mở đầu (ảnh chọn đầu tiên = khung hình đầu của clip; tối đa 3)'}>
                            <AssetPicker compact assets={assets.filter((a) => (form.assetIds ?? []).includes(a.id) || s.assetIds.includes(a.id))}
                              value={s.assetIds} onChange={(ids) => updateShot(i, { assetIds: ids.slice(0, 3) })} />
                          </Field>
                          {!(s.continueFromPrevious && i > 0) && s.assetIds[0] && assetById.has(s.assetIds[0]) && (
                            <p className="text-[11px] text-fg-muted">Khung mở đầu: <b>{assetById.get(s.assetIds[0])!.name}</b></p>
                          )}
                          {s.assetIds.some((id) => !assetById.has(id)) && <p className="text-[11px] text-warning">Một ảnh của shot đã bị xoá khỏi kho.</p>}
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

          {/* 3. Generate + final */}
          <Card className="space-y-4 p-4">
            <div className="flex items-center gap-2 text-sm font-semibold text-fg">
              <Film className="h-4 w-4" /> 3. Tạo video
            </div>
            <p className="text-xs text-fg-muted">
              Mỗi shot được tạo bằng <b>Grok Imagine 1.5 Lite</b> (hình + tiếng + lời thoại khớp miệng), rồi ghép lại với nhạc nền và xuất 1080p. Còn {pendingSeconds}s chưa tạo, ước tính{' '}
              <b>{usd(pendingCost)}</b>
              {remaining != null && <> · ngân sách tháng còn <b>{usd(remaining)}</b></>}.
            </p>
            <div className="flex flex-wrap gap-2">
              <Button variant="primary" icon={<Sparkles className="h-4 w-4" />} loading={busy === 'generate'} disabled={locked || shots.length === 0} onClick={generate}>
                {video.shots.some((s) => s.hasClip) ? 'Tạo các clip còn thiếu' : 'Tạo video'}
              </Button>
              <Button variant="secondary" icon={<RefreshCw className="h-4 w-4" />} loading={busy === 'compose'}
                disabled={locked || video.shots.length === 0 || video.shots.some((s) => !s.hasClip)} onClick={compose}>
                Ghép lại (đổi nhạc / thứ tự)
              </Button>
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

          {/* 4. Publish */}
          <Card className="space-y-3 p-4">
            <div className="flex items-center gap-2 text-sm font-semibold text-fg">
              <Send className="h-4 w-4" /> 4. Đăng lên Facebook
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
    </div>
  );
}
