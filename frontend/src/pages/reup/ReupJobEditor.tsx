import { useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  Download,
  Eye,
  Info,
  Lock,
  Play,
  Plus,
  RotateCcw,
  Save,
  Square,
  Trash2,
  UserRound,
  XCircle,
} from 'lucide-react';
import {
  Alert,
  Badge,
  Button,
  ButtonLink,
  Card,
  CardBody,
  Checkbox,
  Field,
  Input,
  ProgressBar,
  RadioCard,
  Segmented,
  Select,
  Skeleton,
  Thumb,
  cn,
} from '../../components/ui';
import { useConfirm, useToast } from '../../components/feedback';
import { href, navigate } from '../../lib/router';
import { formatBytes, formatDateTime } from '../../lib/format';
import {
  AudioMode,
  CATEGORIES,
  JobInput,
  Placement,
  ReupBackdrop,
  ReupCharacter,
  ReupJob,
  categoryLabel,
  isActive,
  reup,
} from '../../services/reup';
import { useReup } from './ReupPage';
import { Dropzone, JobStatusBadge, SectionTitle, Slider, minutesText, seconds } from './ReupBits';

// ═══════════════════════════════ new job (upload) ═══════════════════════════════

export function ReupNewJob() {
  const { fail } = useReup();
  const [title, setTitle] = useState('');
  const [progress, setProgress] = useState<number | null>(null);

  const upload = async (file: File) => {
    setProgress(0);
    try {
      const job = await reup.createJob(file, title, setProgress);
      navigate(`/reup/jobs/${job.id}`);
    } catch (e) {
      fail(e, 'Không tải được video lên.');
    } finally {
      setProgress(null);
    }
  };

  return (
    <Card>
      <CardBody className="space-y-4">
        <SectionTitle n={1} title="Video nguồn" description="MP4 / MOV / WEBM, tối đa 600 MB và 5 phút. Nên dùng video có một người, quay rõ, đủ sáng." />
        <Field label="Tên video" optional>
          <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Mặc định: tên file" />
        </Field>
        <Dropzone
          accept="video/*"
          label="Kéo thả video vào đây, hoặc bấm để chọn"
          hint="Chỉ dùng video bạn có quyền sử dụng và chỉnh sửa. Bạn sẽ xác nhận quyền ở bước tiếp theo."
          progress={progress}
          onFile={upload}
        />
      </CardBody>
    </Card>
  );
}

// ═══════════════════════════════ job editor ═══════════════════════════════

const defaultPlacement: Placement = { scale: 1, offsetX: 0, offsetY: 0, harmonize: 0.5, lightWrap: 0.3, blur: 2.5, crossfade: 0.6, kenBurns: true, quality: 'high' };

function inputOf(j: ReupJob): JobInput {
  return {
    title: j.title,
    rightsConfirmed: j.rightsConfirmed,
    peopleConsentConfirmed: j.peopleConsentConfirmed,
    rightsNote: j.rightsNote ?? '',
    faceSwapEnabled: j.faceSwapEnabled,
    characterId: j.characterId ?? null,
    backgroundEnabled: j.backgroundEnabled,
    segments: j.segments ?? [],
    placement: { ...defaultPlacement, ...(j.placement ?? {}) },
    audioMode: j.audioMode,
    audioVolume: j.audioVolume,
    originalVolume: j.originalVolume,
    outputShortSide: j.outputShortSide,
  };
}

export function ReupJobEditor({ jobId }: { jobId: string }) {
  const { overview, fail, refreshOverview } = useReup();
  const toast = useToast();
  const confirm = useConfirm();
  const [job, setJob] = useState<ReupJob | null>(null);
  const [draft, setDraft] = useState<JobInput | null>(null);
  const [characters, setCharacters] = useState<ReupCharacter[]>([]);
  const [backdrops, setBackdrops] = useState<ReupBackdrop[]>([]);
  const [busy, setBusy] = useState<string | null>(null);
  const [audioProgress, setAudioProgress] = useState<number | null>(null);
  const [category, setCategory] = useState('');

  const load = async () => {
    try {
      const j = await reup.job(jobId);
      setJob(j);
      setDraft((d) => (d && !isActive(j.status) ? d : inputOf(j)));
    } catch (e) {
      fail(e, 'Không tải được video.');
    }
  };

  useEffect(() => {
    load();
    reup.characters().then(setCharacters).catch(() => undefined);
    reup.backdrops().then(setBackdrops).catch(() => undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [jobId]);

  // follow the run while the PC works on it
  useEffect(() => {
    if (!job || !isActive(job.status)) return;
    const t = setInterval(async () => {
      try {
        const j = await reup.job(jobId);
        setJob(j);
        if (!isActive(j.status)) {
          setDraft(inputOf(j));
          refreshOverview();
          if (j.status === 'Done') toast.success(j.previewRun ? 'Bản xem thử đã sẵn sàng.' : 'Video đã xử lý xong và qua kiểm tra.');
          if (j.status === 'Failed') toast.error('Xử lý video bị lỗi.', j.errorMessage ?? undefined);
        }
      } catch {
        /* keep polling */
      }
    }, 2000);
    return () => clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [job?.status, jobId]);

  const dirty = useMemo(() => job && draft && JSON.stringify(inputOf(job)) !== JSON.stringify(draft), [job, draft]);

  if (!job || !draft) return <Skeleton className="h-96 w-full" />;
  const running = isActive(job.status);
  const locked = running;
  const set = (patch: Partial<JobInput>) => setDraft({ ...draft, ...patch });
  const setPlacement = (patch: Partial<Placement>) => setDraft({ ...draft, placement: { ...draft.placement, ...patch } });

  const save = async (quiet = false) => {
    setBusy('save');
    try {
      const j = await reup.updateJob(job.id, draft);
      setJob(j);
      setDraft(inputOf(j));
      if (!quiet) toast.success('Đã lưu.');
      return true;
    } catch (e) {
      fail(e, 'Không lưu được.');
      return false;
    } finally {
      setBusy(null);
    }
  };

  const run = async (preview: boolean) => {
    if (dirty && !(await save(true))) return;
    setBusy(preview ? 'preview' : 'start');
    try {
      setJob(await (preview ? reup.preview(job.id) : reup.start(job.id)));
      refreshOverview();
    } catch (e) {
      fail(e, 'Không bắt đầu được.');
    } finally {
      setBusy(null);
    }
  };

  const cancel = async () => {
    setBusy('cancel');
    try {
      setJob(await reup.cancel(job.id));
    } catch (e) {
      fail(e);
    } finally {
      setBusy(null);
    }
  };

  const remove = async () => {
    if (!(await confirm({ title: 'Xoá video này?', description: 'Xoá cả video nguồn, bản xem thử và video kết quả khỏi kho file.', confirmLabel: 'Xoá', danger: true }))) return;
    try {
      await reup.deleteJob(job.id);
      refreshOverview();
      navigate('/reup/history');
    } catch (e) {
      fail(e);
    }
  };

  const uploadAudio = async (file: File) => {
    setAudioProgress(0);
    try {
      const j = await reup.setAudio(job.id, file, setAudioProgress);
      setJob(j);
      setDraft({ ...draft, audioMode: j.audioMode });
    } catch (e) {
      fail(e, 'Không tải được file âm thanh.');
    } finally {
      setAudioProgress(null);
    }
  };

  const faceSwapOk = overview?.faceSwapAvailable ?? false;
  const readyChars = characters.filter((c) => c.status === 'Ready');
  const bdById = new Map(backdrops.map((b) => [b.id, b]));
  const shownBackdrops = category ? backdrops.filter((b) => b.category === category) : backdrops;
  const singleMode = draft.segments.length <= 1;
  const srcShort = Math.min(job.sourceWidth, job.sourceHeight);
  const res = draft.outputShortSide >= 1080 && srcShort >= 1080 ? '1080p' : '720p';
  const benchFps = overview?.engine.backgroundFps?.[draft.placement.quality === 'fast' ? res : `${res}-high`] ?? overview?.engine.backgroundFps?.[res];
  const frames = job.sourceDurationSeconds * (job.sourceFpsValue || 30);
  const eta = draft.backgroundEnabled && benchFps ? (frames / benchFps) * 1.15 : null;
  const version = job.finishedAtUtc ?? undefined;

  const pickSingle = (id: string) => set({ segments: [{ start: 0, backdropId: id }] });

  return (
    <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_380px]">
      <div className="space-y-5">
        {/* ── 1. source + statements ── */}
        <Card>
          <CardBody className="space-y-4">
            <SectionTitle
              n={1}
              title="Video nguồn"
              aside={
                <div className="flex items-center gap-2">
                  <JobStatusBadge job={job} />
                  <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-4 w-4" />} onClick={remove} disabled={running}>
                    Xoá
                  </Button>
                </div>
              }
            />
            <div className="flex flex-col gap-4 sm:flex-row">
              {job.filesDeleted ? (
                <Thumb src={job.hasSourceThumb ? reup.thumbUrl(job.id) : null} className="aspect-[9/16] w-full max-w-[160px] rounded-lg" />
              ) : (
                <video src={reup.sourceUrl(job.id)} poster={job.hasSourceThumb ? reup.thumbUrl(job.id) : undefined} controls preload="none" className="aspect-[9/16] w-full max-w-[160px] rounded-lg bg-black object-contain" />
              )}
              <div className="min-w-0 flex-1 space-y-3">
                <Field label="Tên video">
                  <Input value={draft.title ?? ''} onChange={(e) => set({ title: e.target.value })} disabled={locked} />
                </Field>
                <div className="flex flex-wrap gap-1.5 text-xs">
                  <Badge>{seconds(job.sourceDurationSeconds)}</Badge>
                  <Badge>
                    {job.sourceWidth}×{job.sourceHeight}
                  </Badge>
                  <Badge>{job.sourceFpsValue} fps</Badge>
                  <Badge tone={job.sourceHasAudio ? 'neutral' : 'warning'}>{job.sourceHasAudio ? 'Có âm thanh' : 'Không âm thanh'}</Badge>
                  <Badge>{formatBytes(job.sourceSizeBytes)}</Badge>
                </div>
                {job.filesDeleted && <Alert tone="warning">File nguồn đã hết hạn lưu và bị xoá — tải video lên lại để xử lý tiếp.</Alert>}
              </div>
            </div>
            <div className="space-y-2.5 rounded-lg border border-line bg-subtle p-3">
              <Checkbox
                checked={draft.rightsConfirmed}
                onChange={(v) => set({ rightsConfirmed: v })}
                disabled={locked}
                label="Tôi có quyền sử dụng và chỉnh sửa video này"
                description="Video của bạn, hoặc được chủ sở hữu cho phép chỉnh sửa và đăng lại."
              />
              <Checkbox
                checked={draft.peopleConsentConfirmed}
                onChange={(v) => set({ peopleConsentConfirmed: v })}
                disabled={locked}
                label="Người xuất hiện trong video đã đồng ý cho chỉnh sửa (thay nền / thay mặt) và đăng lại"
              />
              <Input
                value={draft.rightsNote ?? ''}
                onChange={(e) => set({ rightsNote: e.target.value })}
                disabled={locked}
                placeholder="Ghi chú nguồn / giấy phép (không bắt buộc) — ví dụ: video tự quay, hợp đồng với KOL…"
              />
            </div>
          </CardBody>
        </Card>

        {/* ── 2. face swap ── */}
        <Card>
          <CardBody className="space-y-3">
            <SectionTitle
              n={2}
              title="Thay khuôn mặt"
              description="Thay mặt người trong video bằng nhân vật AI, giữ chuyển động đầu, biểu cảm và cơ thể."
              aside={<Checkbox checked={draft.faceSwapEnabled} onChange={(v) => set({ faceSwapEnabled: v })} disabled={locked || !faceSwapOk} label="Bật" />}
            />
            {!faceSwapOk && (
              <Alert tone="warning" icon={<Lock />} title="Đang khoá vì giấy phép">
                {overview?.faceSwapBlockedReason}{' '}
                <a className="text-accent hover:underline" href={href('/reup/settings')}>
                  Cài đặt →
                </a>
              </Alert>
            )}
            {draft.faceSwapEnabled && (
              <>
                {readyChars.length === 0 ? (
                  <Alert tone="info" icon={<UserRound />}>
                    Chưa có nhân vật nào qua kiểm tra khuôn mặt —{' '}
                    <a className="text-accent hover:underline" href={href('/reup/characters')}>
                      thêm nhân vật
                    </a>
                    .
                  </Alert>
                ) : (
                  <div className="grid grid-cols-3 gap-2 sm:grid-cols-5">
                    {readyChars.map((c) => (
                      <button
                        key={c.id}
                        type="button"
                        disabled={locked}
                        onClick={() => set({ characterId: c.id })}
                        className={cn(
                          'overflow-hidden rounded-lg border text-left transition-all',
                          draft.characterId === c.id ? 'border-accent ring-2 ring-accent/50' : 'border-line hover:border-line-strong'
                        )}
                      >
                        <Thumb src={c.hasThumb ? reup.characterThumbUrl(c.id) : reup.characterImageUrl(c.id)} className="aspect-square w-full" />
                        <div className="truncate px-2 py-1 text-xs font-medium text-fg">{c.name}</div>
                      </button>
                    ))}
                  </div>
                )}
                <p className="text-xs text-fg-subtle">
                  Khi mặt quay nghiêng mạnh, quay lưng, bị che hoặc quá nhỏ, khung hình được giữ nguyên (không tạo mặt lỗi). Chỉ người chính được thay; người khác giữ nguyên.
                </p>
              </>
            )}
          </CardBody>
        </Card>

        {/* ── 3. background ── */}
        <Card>
          <CardBody className="space-y-4">
            <SectionTitle
              n={3}
              title="Thay cảnh nền"
              description="Tách người khỏi nền cũ và đặt vào cảnh mới (ảnh hoặc video)."
              aside={<Checkbox checked={draft.backgroundEnabled} onChange={(v) => set({ backgroundEnabled: v })} disabled={locked} label="Bật" />}
            />
            {draft.backgroundEnabled && (
              <>
                {backdrops.length === 0 ? (
                  <Alert tone="info">
                    Thư viện cảnh đang trống —{' '}
                    <a className="text-accent hover:underline" href={href('/reup/backdrops')}>
                      thêm cảnh
                    </a>{' '}
                    (tải lên, dán link hoặc tìm trên Pexels / Pixabay).
                  </Alert>
                ) : (
                  <>
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <Segmented
                        size="sm"
                        value={singleMode ? 'single' : 'multi'}
                        onChange={(v) => {
                          if (v === 'single') set({ segments: draft.segments.slice(0, 1) });
                          else if (draft.segments.length < 2) {
                            const first = draft.segments[0]?.backdropId ?? backdrops[0].id;
                            set({ segments: [{ start: 0, backdropId: first }, { start: Math.round(job.sourceDurationSeconds / 2), backdropId: first }] });
                          }
                        }}
                        options={[
                          { value: 'single', label: 'Một cảnh cho cả video' },
                          { value: 'multi', label: 'Đổi cảnh theo đoạn' },
                        ]}
                      />
                      <Select size="sm" value={category} onChange={(e) => setCategory(e.target.value)} className="w-44">
                        <option value="">Mọi loại cảnh</option>
                        {CATEGORIES.map((c) => (
                          <option key={c.id} value={c.id}>
                            {c.label}
                          </option>
                        ))}
                      </Select>
                    </div>
                    {singleMode ? (
                      <BackdropGrid items={shownBackdrops} selected={draft.segments[0]?.backdropId} onPick={pickSingle} disabled={locked} />
                    ) : (
                      <div className="space-y-2">
                        {draft.segments.map((s, i) => (
                          <div key={i} className="flex flex-wrap items-center gap-2 rounded-lg border border-line p-2">
                            <span className="w-14 text-xs text-fg-muted">Đoạn {i + 1}</span>
                            <div className="w-24 shrink-0">
                              <Input
                                type="number"
                                min={0}
                                step={0.5}
                                disabled={locked || i === 0}
                                value={s.start}
                                onChange={(e) => {
                                  const segs = [...draft.segments];
                                  segs[i] = { ...s, start: Number(e.target.value) };
                                  set({ segments: segs });
                                }}
                              />
                            </div>
                            <span className="text-xs text-fg-muted">giây →</span>
                            <Select
                              size="sm"
                              value={s.backdropId}
                              disabled={locked}
                              onChange={(e) => {
                                const segs = [...draft.segments];
                                segs[i] = { ...s, backdropId: e.target.value };
                                set({ segments: segs });
                              }}
                              className="min-w-[180px] flex-1"
                            >
                              {shownBackdrops.concat(bdById.has(s.backdropId) && !shownBackdrops.some((b) => b.id === s.backdropId) ? [bdById.get(s.backdropId)!] : []).map((b) => (
                                <option key={b.id} value={b.id}>
                                  {b.name} · {categoryLabel(b.category)} · {b.kind === 'Video' ? 'video' : 'ảnh'}
                                </option>
                              ))}
                            </Select>
                            <Thumb src={bdById.get(s.backdropId)?.hasThumb ? reup.backdropThumbUrl(s.backdropId) : null} className="h-10 w-7 rounded" />
                            {i > 0 && (
                              <Button size="sm" variant="ghost" disabled={locked} onClick={() => set({ segments: draft.segments.filter((_, k) => k !== i) })}>
                                <Trash2 className="h-4 w-4" />
                              </Button>
                            )}
                          </div>
                        ))}
                        <Button
                          size="sm"
                          icon={<Plus className="h-4 w-4" />}
                          disabled={locked}
                          onClick={() => {
                            const last = draft.segments[draft.segments.length - 1];
                            const next = Math.min(job.sourceDurationSeconds - 0.5, (last?.start ?? 0) + 5);
                            set({ segments: [...draft.segments, { start: Math.round(next * 2) / 2, backdropId: last?.backdropId ?? backdrops[0].id }] });
                          }}
                        >
                          Thêm đoạn
                        </Button>
                        <p className="text-xs text-fg-subtle">Mỗi đoạn bắt đầu tại giây đã nhập và kéo dài tới đoạn sau; giữa hai cảnh có chuyển cảnh mờ dần.</p>
                      </div>
                    )}
                  </>
                )}
                <div className="space-y-1.5 border-t border-line pt-4">
                  <div className="text-[13px] font-medium text-fg">Chất lượng tách nền</div>
                  <Segmented
                    value={draft.placement.quality}
                    onChange={(v) => !locked && setPlacement({ quality: v })}
                    options={[
                      { value: 'high', label: 'Đẹp (chậm hơn)' },
                      { value: 'fast', label: 'Nhanh' },
                    ]}
                  />
                  <p className="text-xs text-fg-subtle">Đẹp: viền tóc và mép người sạch hơn, xử lý lâu hơn khoảng 2,5 lần.</p>
                </div>
                <div className="grid gap-4 sm:grid-cols-2">
                  <Slider label="Cỡ người" value={draft.placement.scale} min={0.5} max={1.3} step={0.05} onChange={(v) => setPlacement({ scale: v })} format={(v) => `${Math.round(v * 100)}%`} hint="Giữ chân người ở cạnh dưới khi đổi cỡ" disabled={locked} />
                  <Slider label="Lệch ngang" value={draft.placement.offsetX} min={-0.4} max={0.4} step={0.02} onChange={(v) => setPlacement({ offsetX: v })} format={(v) => `${Math.round(v * 100)}%`} disabled={locked} />
                  <Slider label="Lệch dọc" value={draft.placement.offsetY} min={-0.3} max={0.3} step={0.02} onChange={(v) => setPlacement({ offsetY: v })} format={(v) => `${Math.round(v * 100)}%`} disabled={locked} />
                  <Slider label="Hoà màu với cảnh mới" value={draft.placement.harmonize} min={0} max={1} step={0.05} onChange={(v) => setPlacement({ harmonize: v })} format={(v) => `${Math.round(v * 100)}%`} hint="Kéo sáng / tông màu người về gần cảnh" disabled={locked} />
                  <Slider label="Ánh sáng viền" value={draft.placement.lightWrap} min={0} max={1} step={0.05} onChange={(v) => setPlacement({ lightWrap: v })} format={(v) => `${Math.round(v * 100)}%`} hint="Ánh sáng cảnh tràn nhẹ lên mép người — bớt cảm giác cắt dán" disabled={locked} />
                  <Slider label="Làm mờ nền" value={draft.placement.blur} min={0} max={10} step={0.5} onChange={(v) => setPlacement({ blur: v })} format={(v) => (v ? v.toFixed(1) : 'tắt')} hint="Như chụp xoá phông — che bớt chênh lệch" disabled={locked} />
                  <Slider label="Chuyển cảnh" value={draft.placement.crossfade} min={0} max={2} step={0.1} onChange={(v) => setPlacement({ crossfade: v })} format={(v) => `${v.toFixed(1)}s`} disabled={locked || singleMode} />
                  <div className="self-end">
                    <Checkbox checked={draft.placement.kenBurns} onChange={(v) => setPlacement({ kenBurns: v })} disabled={locked} label="Zoom chậm ảnh nền" description="Ảnh tĩnh nhích nhẹ để không bị 'đứng hình'" />
                  </div>
                </div>
                <Alert tone="neutral" icon={<Info />}>
                  Giới hạn thật: model tách <b>người</b>, nên đồ vật cầm trên tay có thể mờ hoặc mất một phần; không tạo bóng đổ. Nếu camera gốc rung / lia
                  mạnh mà dùng ảnh tĩnh làm nền sẽ dễ lộ — nên chọn video nền. Bấm <b>Xem thử 5 giây</b> để kiểm tra trước.
                </Alert>
              </>
            )}
          </CardBody>
        </Card>

        {/* ── 4. audio ── */}
        <Card>
          <CardBody className="space-y-3">
            <SectionTitle n={4} title="Âm thanh" />
            <div className="grid gap-2 sm:grid-cols-2">
              {(
                [
                  ['Remove', 'Không âm thanh', 'Xuất video câm — chọn nhạc trong thư viện của Facebook khi đăng.'],
                  ['KeepOriginal', 'Giữ âm thanh gốc', 'Chỉ khi bạn có quyền với âm thanh / nhạc trong video gốc.'],
                  ['Replace', 'Dùng file âm thanh riêng', 'Thay hẳn bằng file bạn tải lên (nhạc / lời có bản quyền của bạn).'],
                  ['Mix', 'Trộn gốc + file riêng', 'Giữ tiếng gốc và thêm file của bạn.'],
                ] as [AudioMode, string, string][]
              ).map(([mode, title, desc]) => (
                <RadioCard
                  key={mode}
                  checked={draft.audioMode === mode}
                  onClick={() => !locked && set({ audioMode: mode })}
                  title={title}
                  description={desc}
                  aside={(mode === 'KeepOriginal' || mode === 'Mix') && !job.sourceHasAudio ? <Badge tone="warning">nguồn không có tiếng</Badge> : undefined}
                />
              ))}
            </div>
            {(draft.audioMode === 'Replace' || draft.audioMode === 'Mix') && (
              <div className="space-y-2">
                {job.audioFileName ? (
                  <div className="flex flex-wrap items-center gap-2 rounded-lg border border-line p-2 text-sm">
                    <audio src={reup.audioUrl(job.id)} controls className="h-8 max-w-full" />
                    <span className="truncate text-fg-muted">{job.audioFileName}</span>
                    <Button size="sm" variant="ghost" disabled={locked} onClick={async () => setJob(await reup.removeAudio(job.id).catch((e) => (fail(e), job)))}>
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                ) : (
                  <Dropzone accept="audio/*,video/*" label="Chọn file âm thanh (MP3, M4A, WAV…)" hint="Không tự lấy nhạc từ nền tảng khác — chỉ dùng file bạn có quyền." progress={audioProgress} onFile={uploadAudio} disabled={locked} />
                )}
              </div>
            )}
            {draft.audioMode !== 'Remove' && (
              <div className="grid gap-4 sm:grid-cols-2">
                {(draft.audioMode === 'KeepOriginal' || draft.audioMode === 'Mix') && (
                  <Slider label="Âm lượng gốc" value={draft.originalVolume} min={0} max={2} step={0.05} onChange={(v) => set({ originalVolume: v })} format={(v) => `${Math.round(v * 100)}%`} disabled={locked} />
                )}
                {(draft.audioMode === 'Replace' || draft.audioMode === 'Mix') && (
                  <Slider label="Âm lượng file riêng" value={draft.audioVolume} min={0} max={2} step={0.05} onChange={(v) => set({ audioVolume: v })} format={(v) => `${Math.round(v * 100)}%`} disabled={locked} />
                )}
              </div>
            )}
          </CardBody>
        </Card>

        {/* ── 5. output ── */}
        <Card>
          <CardBody className="space-y-3">
            <SectionTitle n={5} title="Xuất video" description="MP4 H.264 · giữ nguyên FPS, tỉ lệ khung hình và thời lượng của video gốc." />
            <Segmented
              value={draft.outputShortSide}
              onChange={(v) => !locked && set({ outputShortSide: v })}
              options={[
                { value: 720, label: '720p' },
                { value: 1080, label: srcShort >= 1080 ? '1080p' : '1080p (nguồn chỉ ' + srcShort + 'p)' },
              ]}
            />
            <p className="text-xs text-fg-subtle">
              Kích thước ra: {job.outputWidth}×{job.outputHeight} (lưu để cập nhật) · {job.sourceFpsValue} fps · {seconds(job.sourceDurationSeconds)}. Không phóng to quá độ phân giải nguồn.
            </p>
          </CardBody>
        </Card>
      </div>

      {/* ── side panel: run + result ── */}
      <div className="space-y-5 xl:sticky xl:top-4 xl:self-start">
        <Card className="overflow-hidden">
          <div className="h-1 bg-gradient-to-r from-indigo-500 via-violet-500 to-fuchsia-500" />
          <CardBody className="space-y-3">
            <div className="flex items-center justify-between">
              <span className="text-sm font-semibold text-fg">Xử lý</span>
              <JobStatusBadge job={job} />
            </div>
            {running ? (
              <>
                <ProgressBar value={job.progressPercent} />
                <div className="text-[13px] text-fg">{job.progressText}</div>
                {job.status === 'Queued' && !overview?.pcOnline && <div className="text-xs text-warning">Máy tính đang tắt — video sẽ chạy khi máy bật.</div>}
                {job.framesTotal > 0 && job.status === 'Processing' && (
                  <div className="text-xs text-fg-muted">
                    {job.framesDone}/{job.framesTotal} khung · {job.fps ? `${job.fps} khung/giây` : '…'} {job.workerMachine ? `· ${job.workerMachine}` : ''}
                  </div>
                )}
                <Button variant="danger-soft" icon={<Square className="h-4 w-4" />} loading={busy === 'cancel'} disabled={job.cancelRequested} onClick={cancel}>
                  {job.cancelRequested ? 'Đang huỷ…' : 'Huỷ'}
                </Button>
              </>
            ) : (
              <>
                {!draft.faceSwapEnabled && !draft.backgroundEnabled && (
                  <Alert tone="warning" icon={<AlertTriangle />}>
                    Chưa bật <b>Thay cảnh nền</b> (mục 3) hay <b>Thay khuôn mặt</b> (mục 2) — video sẽ ra y như cũ. Bật ít nhất một mục.
                  </Alert>
                )}
                {eta && <div className="text-xs text-fg-muted">Ước tính (đo trên máy, thay nền {res}): {minutesText(eta)}{draft.faceSwapEnabled ? ' + thời gian thay mặt' : ''}</div>}
                <div className="flex flex-wrap gap-2">
                  <Button variant="secondary" icon={<Save className="h-4 w-4" />} loading={busy === 'save'} disabled={!dirty} onClick={() => save()}>
                    Lưu
                  </Button>
                  <Button variant="secondary" icon={<Eye className="h-4 w-4" />} loading={busy === 'preview'} onClick={() => run(true)} disabled={job.filesDeleted || (!draft.faceSwapEnabled && !draft.backgroundEnabled)}>
                    Xem thử 5 giây
                  </Button>
                  <Button variant="primary" icon={job.status === 'Done' || job.status === 'Failed' ? <RotateCcw className="h-4 w-4" /> : <Play className="h-4 w-4" />} loading={busy === 'start'} onClick={() => run(false)} disabled={job.filesDeleted || (!draft.faceSwapEnabled && !draft.backgroundEnabled)}>
                    {job.status === 'Done' || job.status === 'Failed' || job.status === 'Cancelled' ? 'Làm lại' : 'Bắt đầu'}
                  </Button>
                </div>
                {job.status === 'Failed' && job.errorMessage && (
                  <Alert tone="danger" icon={<AlertTriangle />} title="Lỗi">
                    {job.errorMessage}
                  </Alert>
                )}
              </>
            )}
          </CardBody>
        </Card>

        {job.hasPreview && (
          <Card>
            <CardBody className="space-y-2">
              <div className="text-sm font-semibold text-fg">Bản xem thử (5 giây đầu)</div>
              <video key={version} src={reup.previewUrl(job.id, version)} controls playsInline className="max-h-[420px] w-full rounded-lg bg-black object-contain" />
            </CardBody>
          </Card>
        )}

        {job.hasOutput && (
          <Card>
            <CardBody className="space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-sm font-semibold text-fg">Video kết quả</span>
                <ButtonLink href={reup.outputUrl(job.id, true, version)} size="sm" variant="primary" icon={<Download className="h-4 w-4" />}>
                  Tải MP4
                </ButtonLink>
              </div>
              <video key={version} src={reup.outputUrl(job.id, false, version)} controls playsInline className="max-h-[520px] w-full rounded-lg bg-black object-contain" />
              <div className="text-xs text-fg-muted">
                {seconds(job.outputDurationSeconds)} · {formatBytes(job.outputSizeBytes ?? 0)}
                {job.expiresAtUtc && ` · tự xoá khỏi kho file lúc ${formatDateTime(job.expiresAtUtc)}`}
              </div>
              <Alert tone="info" icon={<Info />}>
                Khi đăng Facebook: bật nhãn <b>“Thông tin AI”</b> — Meta yêu cầu với video người thật đã chỉnh sửa bằng AI. Video chỉ đổi nền / đăng lại
                có thể bị Facebook xếp vào nội dung “không nguyên bản” (giảm phân phối, mất kiếm tiền).
              </Alert>
            </CardBody>
          </Card>
        )}

        {job.qc.length > 0 && (
          <Card>
            <CardBody className="space-y-1.5">
              <div className="text-sm font-semibold text-fg">Kiểm tra tự động</div>
              {job.qc.map((c) => (
                <div key={c.name} className="flex items-start gap-2 text-[13px]">
                  {c.ok ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-success" /> : <XCircle className="mt-0.5 h-4 w-4 shrink-0 text-danger" />}
                  <span className="text-fg">{c.name}</span>
                  <span className="ml-auto text-right text-xs text-fg-muted">{c.detail}</span>
                </div>
              ))}
              {job.stats && (
                <div className="border-t border-line pt-2 text-xs text-fg-muted">
                  {job.stats.frames} khung trong {seconds(job.stats.seconds)} ({job.stats.fps} khung/giây)
                  {job.stats.faceSwapped != null &&
                    ` · thay mặt ${job.stats.faceSwapped} khung, giữ nguyên ${(job.stats.faceKeptOriginal ?? 0) + (job.stats.noFace ?? 0)} khung (mặt không đủ điều kiện / không thấy mặt)`}
                </div>
              )}
            </CardBody>
          </Card>
        )}
      </div>
    </div>
  );
}

function BackdropGrid({ items, selected, onPick, disabled }: { items: ReupBackdrop[]; selected?: string; onPick: (id: string) => void; disabled?: boolean }) {
  if (items.length === 0) return <p className="text-sm text-fg-muted">Không có cảnh nào trong loại này.</p>;
  return (
    <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-6">
      {items.map((b) => (
        <button
          key={b.id}
          type="button"
          disabled={disabled}
          onClick={() => onPick(b.id)}
          className={cn('group overflow-hidden rounded-lg border text-left transition-all', selected === b.id ? 'border-accent ring-2 ring-accent/50' : 'border-line hover:border-line-strong')}
        >
          <div className="relative">
            <Thumb src={b.hasThumb ? reup.backdropThumbUrl(b.id) : reup.backdropFileUrl(b.id)} className="aspect-[9/16] w-full" />
            {b.kind === 'Video' && <span className="absolute right-1 top-1 rounded bg-black/60 px-1 text-[10px] text-white">video {seconds(b.durationSeconds)}</span>}
          </div>
          <div className="truncate px-1.5 py-1 text-[11px] text-fg">{b.name}</div>
        </button>
      ))}
    </div>
  );
}
