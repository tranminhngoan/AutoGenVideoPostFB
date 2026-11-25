import { useCallback, useEffect, useRef, useState } from 'react';
import { AlertTriangle, ArrowLeft, Brain, Clapperboard, FileVideo, Pencil, Play, RefreshCw, Replace, Square, Trash2, Users, Palette, MapPin } from 'lucide-react';
import { Alert, Button, ButtonLink, Field, Input, Modal, ProgressBar, Segmented, Skeleton, Textarea, cn } from '../../components/ui';
import { useConfirm, useToast } from '../../components/feedback';
import { href, navigate } from '../../lib/router';
import { formatBytes, formatDateTime } from '../../lib/format';
import { StudioError } from '../../services/studio';
import { aiStudio, isAnalyzing, isGenActive, type Json, type Project } from '../../services/aiStudio';
import { Chip, ProjectStatusBadge, Step, Swatches, fact, sec, useAiStudio } from './bits';
import { Storyboard } from './Storyboard';
import { GeneratorPanel, HistoryTable } from './Generator';

export interface WorkspaceProps {
  project: Project;
  setProject: (p: Project) => void;
  reload: () => Promise<void>;
  busy: boolean;
}

export function Workspace({ projectId }: { projectId: string }) {
  const [project, setProject] = useState<Project | null>(null);
  const [error, setError] = useState<string | null>(null);
  const { onKeyNeeded } = useAiStudio();

  const reload = useCallback(async () => {
    try {
      setProject(await aiStudio.project(projectId));
      setError(null);
    } catch (e) {
      if (e instanceof StudioError && e.needsKey) onKeyNeeded();
      setError((e as Error).message);
    }
  }, [projectId, onKeyNeeded]);

  useEffect(() => {
    reload();
  }, [reload]);

  // live while something runs (analysis on the PC, clips at Seedance, joining)
  const active = !!project && (isAnalyzing(project.status) || project.scenes.some((s) => isGenActive(s.latest?.status)) || project.finalStatus === 'Assembling');
  useEffect(() => {
    if (!active) return;
    const t = setInterval(reload, 3000);
    return () => clearInterval(t);
  }, [active, reload]);

  useEffect(() => {
    if (project) document.title = `${project.title} · AI Video Studio`;
  }, [project?.title]);

  if (error && !project) return <Alert tone="danger" title="Không mở được dự án">{error}</Alert>;
  if (!project) return <Skeleton className="h-96 w-full" />;

  const analyzed = project.status === 'Analyzed';
  const props: WorkspaceProps = { project, setProject, reload, busy: isAnalyzing(project.status) };
  const steps = [
    { id: 'reference', label: 'Video' },
    { id: 'analysis', label: 'Phân tích' },
    { id: 'storyboard', label: 'Storyboard & Prompt' },
    { id: 'generator', label: 'Seedance' },
    { id: 'history', label: 'Lịch sử' },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <ButtonLink href={href('/ai-studio')} variant="ghost" size="sm" icon={<ArrowLeft className="h-4 w-4" />}>Dự án</ButtonLink>
          <h2 className="truncate text-lg font-semibold text-fg">{project.title}</h2>
          <ProjectStatusBadge status={project.status} />
        </div>
      </div>
      <nav className="sticky top-0 z-20 -mx-1 flex gap-1 overflow-x-auto rounded-xl border border-line bg-surface/90 p-1 shadow-card backdrop-blur">
        {steps.map((s, i) => (
          <a key={s.id} href={`#${s.id}`} onClick={(e) => { e.preventDefault(); document.getElementById(s.id)?.scrollIntoView({ behavior: 'smooth' }); }}
            className="inline-flex h-8 shrink-0 items-center gap-2 rounded-lg px-3 text-[13px] text-fg-muted hover:bg-muted hover:text-fg">
            <span className="grid h-5 w-5 place-items-center rounded-full bg-muted text-[11px] font-semibold text-fg">{i + 1}</span>{s.label}
          </a>
        ))}
      </nav>

      <ReferenceStep {...props} />
      <AnalysisStep {...props} />
      <Step id="storyboard" n={3} title="Storyboard & Prompt" dim={!analyzed}
        description="Từng cảnh: khung hình chính, hành động theo thời gian, máy quay đo được, prompt cho Seedance (sửa được, có lịch sử phiên bản).">
        {analyzed ? <Storyboard {...props} /> : <p className="text-sm text-fg-muted">Phân tích video xong thì storyboard hiện ở đây.</p>}
      </Step>
      <Step id="generator" n={4} title="Seedance Generator" dim={!analyzed}
        description="Tạo video mới từ prompt ngay tại đây — chọn model, độ phân giải, xem chi phí trước khi xác nhận.">
        {analyzed ? <GeneratorPanel {...props} /> : <p className="text-sm text-fg-muted">Cần phân tích xong và có prompt trước.</p>}
      </Step>
      <Step id="history" n={5} title="Lịch sử tạo video" description="Mọi lần tạo của dự án: prompt đã dùng, model, thông số, chi phí, video.">
        <HistoryTable projectId={project.id} refreshKey={project.scenes.map((s) => s.latest?.status ?? '').join(',')} />
      </Step>
    </div>
  );
}

// ───────────────────────── 1. reference video ─────────────────────────

function ReferenceStep({ project: p, setProject, busy }: WorkspaceProps) {
  const toast = useToast();
  const confirm = useConfirm();
  const input = useRef<HTMLInputElement>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [title, setTitle] = useState(p.title);

  const replace = async (file?: File | null) => {
    if (!file) return;
    if (p.status === 'Analyzed' && !(await confirm({ title: 'Thay video tham chiếu?', description: 'Phân tích, cảnh và prompt hiện tại sẽ bị xoá (video đã tạo vẫn ở Lịch sử).', confirmLabel: 'Thay video', danger: true }))) return;
    setProgress(0);
    try {
      setProject(await aiStudio.replaceSource(p.id, file, setProgress));
      toast.success('Đã thay video', 'Bấm Phân tích video để chạy lại.');
    } catch (e) {
      toast.error('Không thay được video', (e as Error).message);
    } finally {
      setProgress(null);
    }
  };

  const remove = async () => {
    if (!(await confirm({ title: 'Xoá dự án?', description: 'Xoá video tham chiếu, phân tích, prompt và các clip đã tạo của dự án. Không khôi phục được.', confirmLabel: 'Xoá hẳn', danger: true }))) return;
    try {
      await aiStudio.deleteProject(p.id);
      toast.success('Đã xoá dự án');
      navigate('/ai-studio');
    } catch (e) {
      toast.error('Không xoá được', (e as Error).message);
    }
  };

  const saveTitle = async () => {
    if (!title.trim() || title.trim() === p.title) return;
    try {
      setProject(await aiStudio.updateProject(p.id, { title: title.trim() }));
    } catch (e) {
      toast.error('Không đổi được tên', (e as Error).message);
    }
  };

  const audio = p.audioTracks.length === 0 ? 'Không có'
    : p.audioTracks.map((a) => `${a.codec ?? '?'} ${a.sampleRate ? `${(a.sampleRate / 1000).toFixed(1)} kHz` : ''} ${a.channels ? `${a.channels} kênh` : ''}`.trim()).join(' · ');
  const meta: [string, string][] = [
    ['Thời lượng', sec(p.durationSeconds)],
    ['Dung lượng', formatBytes(p.sourceSizeBytes)],
    ['Độ phân giải', `${p.width}×${p.height}`],
    ['Tỷ lệ khung', p.aspectRatio],
    ['FPS', p.fps ? p.fps.toFixed(p.fps % 1 ? 2 : 0) : '—'],
    ['Định dạng', `${p.sourceContainer.toUpperCase()} · ${p.videoCodec ?? '—'}`],
    ['Bitrate', p.bitrateKbps ? `${(p.bitrateKbps / 1000).toFixed(1)} Mbps` : '—'],
    ['Âm thanh', audio],
  ];

  return (
    <Step id="reference" n={1} title="Video tham chiếu" description={p.sourceFileName}
      actions={<>
        <Button size="sm" variant="secondary" icon={<Replace className="h-4 w-4" />} disabled={busy || progress != null} onClick={() => input.current?.click()}>Thay video</Button>
        <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-4 w-4" />} disabled={busy} onClick={remove}>Xoá dự án</Button>
      </>}>
      <input ref={input} type="file" accept=".mp4,.mov,.webm,video/mp4,video/quicktime,video/webm" hidden onChange={(e) => { replace(e.target.files?.[0]); e.target.value = ''; }} />
      <div className="grid gap-5 md:grid-cols-[minmax(0,320px)_1fr]">
        <div>
          {p.hasSource ? (
            <video key={p.sourceFileName + p.sourceSizeBytes} src={aiStudio.sourceUrl(p.id)} controls playsInline preload="metadata"
              poster={p.hasThumb ? aiStudio.thumbUrl(p.id) : undefined} className="max-h-[420px] w-full rounded-xl bg-black" />
          ) : (
            <div className="grid aspect-[9/16] max-h-[420px] w-full place-items-center rounded-xl bg-muted p-4 text-center text-xs text-fg-muted">
              <div><FileVideo className="mx-auto mb-2 h-6 w-6" />Video tham chiếu đã hết hạn lưu và được xoá khỏi kho (phân tích vẫn giữ). Tải lại nếu muốn phân tích lại.</div>
            </div>
          )}
          {progress != null && <ProgressBar className="mt-2" value={progress * 100} />}
        </div>
        <div className="space-y-4">
          <Field label="Tên dự án">
            <Input value={title} onChange={(e) => setTitle(e.target.value)} onBlur={saveTitle} onKeyDown={(e) => e.key === 'Enter' && saveTitle()} maxLength={300} />
          </Field>
          <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            {meta.map(([k, v]) => (
              <div key={k} className="rounded-lg border border-line bg-subtle px-3 py-2">
                <dt className="text-[11px] text-fg-subtle">{k}</dt>
                <dd className="mt-0.5 truncate text-[13px] font-medium text-fg" title={v}>{v}</dd>
              </div>
            ))}
          </dl>
          <p className="text-[11px] text-fg-subtle">Tải lên {formatDateTime(p.createdAtUtc)}. Video tham chiếu được giữ trong kho 7 ngày (đổi trong Máy phân tích › Cài đặt).</p>
        </div>
      </div>
    </Step>
  );
}

// ───────────────────────── 2. analysis ─────────────────────────

function AnalysisStep({ project: p, setProject }: WorkspaceProps) {
  const toast = useToast();
  const confirm = useConfirm();
  const { overview } = useAiStudio();
  const [model, setModel] = useState<'4b' | '8b'>(p.analysisModel);
  const [working, setWorking] = useState(false);
  const running = isAnalyzing(p.status);
  const engine = overview?.engine;
  const has8b = !!engine?.models?.['8b'];
  // rough time from the measured speed: ~1 scene per 4–5 s of video, ~1 300 tokens written per scene + reading the frames
  const estScenes = Math.max(1, Math.round(p.durationSeconds / 4.5));
  const tps = engine?.tokensPerSecond?.[model] || (model === '8b' ? 3.4 : 20);
  const perScene = 1300 / tps + (model === '8b' ? 70 : 45);
  const eta = Math.round((estScenes * perScene + 60) / 60);

  const start = async (force: boolean) => {
    if (force && !(await confirm({ title: 'Phân tích lại?', description: 'Cảnh, bible và prompt sẽ được thay bằng kết quả mới (prompt bạn tự viết cũng bị thay). Video đã tạo vẫn giữ ở Lịch sử.', confirmLabel: 'Phân tích lại' }))) return;
    setWorking(true);
    try {
      if (model !== p.analysisModel) await aiStudio.updateProject(p.id, { analysisModel: model });
      setProject(await aiStudio.analyze(p.id, force));
    } catch (e) {
      toast.error('Chưa phân tích được', (e as Error).message);
    } finally {
      setWorking(false);
    }
  };

  const cancel = async () => {
    try {
      setProject(await aiStudio.cancelAnalysis(p.id));
    } catch (e) {
      toast.error('Không huỷ được', (e as Error).message);
    }
  };

  const meta = p.analysisMeta ?? {};
  return (
    <Step id="analysis" n={2} title="Phân tích video (trên máy tính của bạn)"
      description="FFmpeg + OpenCV đo cắt cảnh, chuyển động máy quay, màu; model Qwen3-VL chạy trên máy đọc hành động, nhân vật, bối cảnh, ánh sáng. Không tốn tiền, không gửi video ra ngoài."
      actions={p.status === 'Analyzed' && !running ? <Button size="sm" variant="ghost" icon={<RefreshCw className="h-4 w-4" />} loading={working} disabled={!p.hasSource} onClick={() => start(true)}>Phân tích lại</Button> : undefined}>
      <div className="space-y-4">
        {!running && p.status !== 'Analyzed' && (
          <div className="grid gap-4 md:grid-cols-[1fr_auto] md:items-end">
            <div className="space-y-2">
              <div className="text-[13px] font-medium text-fg">Model phân tích</div>
              <Segmented<'4b' | '8b'> value={model} onChange={setModel} options={[
                { value: '4b', label: 'Chuẩn · Qwen3-VL 4B' },
                { value: '8b', label: has8b ? 'Kỹ · Qwen3-VL 8B (chậm ~4×)' : '8B (chưa cài)' },
              ]} />
              <p className="text-xs text-fg-muted">
                Ước tính khoảng <b>{eta} phút</b> cho video {sec(p.durationSeconds)} (đo trên máy: {tps} token/giây).
                {model === '8b' && ' 8B mô tả góc máy và thứ tự hành động sát hơn nhưng chậm hơn nhiều trên card 4 GB.'}
              </p>
            </div>
            <Button variant="accent" size="lg" icon={<Brain className="h-4 w-4" />} loading={working} disabled={!p.hasSource || (model === '8b' && !has8b)} onClick={() => start(false)}>
              Phân tích video
            </Button>
          </div>
        )}
        {overview && !overview.pcOnline && (p.status === 'Draft' || running) && (
          <Alert tone="warning" icon={<AlertTriangle />} title="Máy tính đang tắt">Việc phân tích sẽ chờ đến khi máy tính chạy <code>start-pc-worker.bat</code>.</Alert>
        )}
        {running && (
          <div className="rounded-xl border border-accent/25 bg-accent/[0.05] p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="text-sm font-medium text-fg">{p.progressText ?? 'Đang chuẩn bị…'}</div>
              <Button size="sm" variant="danger-soft" icon={<Square className="h-3.5 w-3.5" />} disabled={p.cancelRequested} onClick={cancel}>{p.cancelRequested ? 'Đang huỷ…' : 'Huỷ'}</Button>
            </div>
            <ProgressBar className="mt-3" value={p.progressPercent} />
            <div className="mt-2 flex flex-wrap gap-x-4 text-xs text-fg-muted">
              <span>{p.progressPercent}%</span>
              {p.scenesTotal > 0 && <span>Cảnh {p.scenesDone}/{p.scenesTotal}</span>}
              {p.workerMachine && <span>Máy {p.workerMachine}</span>}
              <span>Model {p.analysisModel.toUpperCase()}</span>
            </div>
          </div>
        )}
        {p.status === 'Failed' && p.errorMessage && <Alert tone="danger" title="Phân tích lỗi" action={<Button size="sm" onClick={() => start(false)} icon={<Play className="h-4 w-4" />}>Chạy lại</Button>}>{p.errorMessage}</Alert>}
        {p.status === 'Cancelled' && <Alert tone="warning" title="Đã huỷ">Bấm Phân tích video để chạy tiếp — các cảnh đã phân tích xong được giữ lại, không làm lại.</Alert>}

        {p.status === 'Analyzed' && (
          <>
            <div className="grid gap-3 lg:grid-cols-[1.3fr_1fr]">
              <div className="rounded-xl border border-line bg-subtle p-4">
                <div className="flex items-center gap-2 text-[13px] font-semibold text-fg"><Clapperboard className="h-4 w-4 text-accent" />Tổng quan</div>
                {fact(p.overview?.summary_vi) && <p className="mt-2 text-sm leading-relaxed text-fg">{p.overview.summary_vi}</p>}
                {fact(p.overview?.summary) && <p className="mt-1.5 text-xs leading-relaxed text-fg-muted">{p.overview.summary}</p>}
                {fact(p.overview?.scene_relations) && <p className="mt-1.5 text-xs text-fg-muted">Liên kết cảnh: {p.overview.scene_relations}</p>}
              </div>
              <div className="rounded-xl border border-line bg-subtle p-4 text-xs text-fg-muted">
                <div className="flex flex-wrap gap-1.5">
                  <Chip>{p.scenes.length} cảnh</Chip>
                  {meta.model && <Chip>Qwen3-VL {String(meta.model).toUpperCase()}</Chip>}
                  {meta.backend && <Chip>{meta.backend === 'vulkan' ? 'Card đồ hoạ (Vulkan)' : 'CPU'}</Chip>}
                  {meta.seconds && <Chip>{Math.round(meta.seconds / 60)} phút {Math.round(meta.seconds % 60)} giây</Chip>}
                  {meta.genTokensPerSecond ? <Chip>{meta.genTokensPerSecond} token/s</Chip> : null}
                </div>
                {p.palette.length > 0 && <div className="mt-3 flex items-center gap-2">Màu đo được <Swatches colors={p.palette} /></div>}
                {p.transcript?.segments?.length > 0 && (
                  <div className="mt-3">
                    <div className="font-medium text-fg">Lời nói ({p.transcript.language ?? '?'})</div>
                    <p className="mt-1 line-clamp-4">{p.transcript.segments.map((s: Json) => s.text).join(' ')}</p>
                  </div>
                )}
                {p.analysisFinishedAtUtc && <div className="mt-3 text-fg-subtle">Xong lúc {formatDateTime(p.analysisFinishedAtUtc)}</div>}
              </div>
            </div>
            <Bibles {...{ project: p, setProject }} />
          </>
        )}
      </div>
    </Step>
  );
}

// ───────────────────────── bibles ─────────────────────────

const CHAR_FIELDS: [string, string][] = [['label', 'Nhân vật'], ['appearance', 'Ngoại hình'], ['hair', 'Tóc'], ['clothing', 'Trang phục'], ['accessories', 'Phụ kiện'], ['posture', 'Tư thế'], ['distinguishing', 'Đặc điểm nhận biết'], ['consistency', 'Giữ nhất quán']];
const ENV_FIELDS: [string, string][] = [['location', 'Địa điểm'], ['layout', 'Bố cục không gian'], ['background', 'Hậu cảnh'], ['important_objects', 'Đồ vật quan trọng'], ['lighting', 'Ánh sáng'], ['weather', 'Thời tiết'], ['time_of_day', 'Thời điểm']];
const STYLE_FIELDS: [string, string][] = [['visual_style', 'Phong cách hình ảnh'], ['realism_level', 'Mức chân thực'], ['color_palette', 'Bảng màu'], ['lighting_style', 'Kiểu ánh sáng'], ['camera_language', 'Ngôn ngữ máy quay'], ['motion_style', 'Chuyển động'], ['depth_of_field', 'Độ sâu trường ảnh'], ['direction', 'Định hướng điện ảnh']];

function Bibles({ project: p, setProject }: { project: Project; setProject: (p: Project) => void }) {
  const [editing, setEditing] = useState<'characters' | 'environments' | 'style' | null>(null);
  const chars: Json[] = Array.isArray(p.characters) ? p.characters : [];
  const envs: Json[] = Array.isArray(p.environments) ? p.environments : [];
  const style: Json = p.style ?? {};

  const Entry = ({ item, fields, idKey }: { item: Json; fields: [string, string][]; idKey?: string }) => (
    <div className="rounded-lg border border-line bg-surface p-3">
      {idKey && <div className="mb-1.5 flex items-center gap-2"><Chip className="font-semibold text-fg">{item[idKey] ?? '?'}</Chip>
        {Array.isArray(item.scenes) && item.scenes.length > 0 && <span className="text-[11px] text-fg-subtle">cảnh {item.scenes.join(', ')}</span>}</div>}
      <dl className="space-y-1">
        {fields.map(([k, label]) => fact(item[k]) && (
          <div key={k} className="grid grid-cols-[110px_1fr] gap-2 text-xs"><dt className="text-fg-subtle">{label}</dt><dd className="text-fg">{fact(item[k])}</dd></div>
        ))}
      </dl>
    </div>
  );

  return (
    <div className="grid gap-3 xl:grid-cols-3">
      <BibleCard icon={<Users className="h-4 w-4" />} title="Character Bible" onEdit={() => setEditing('characters')}>
        {chars.length === 0 ? <p className="text-xs text-fg-muted">Không thấy nhân vật.</p> : chars.map((c, i) => <Entry key={i} item={c} fields={CHAR_FIELDS} idKey="id" />)}
      </BibleCard>
      <BibleCard icon={<MapPin className="h-4 w-4" />} title="Environment Bible" onEdit={() => setEditing('environments')}>
        {envs.length === 0 ? <p className="text-xs text-fg-muted">Chưa có bối cảnh.</p> : envs.map((e, i) => <Entry key={i} item={e} fields={ENV_FIELDS} idKey="id" />)}
      </BibleCard>
      <BibleCard icon={<Palette className="h-4 w-4" />} title="Visual Style Bible" onEdit={() => setEditing('style')}>
        <Entry item={style} fields={STYLE_FIELDS} />
      </BibleCard>
      {editing && (
        <BibleEditor
          kind={editing}
          value={editing === 'characters' ? chars : editing === 'environments' ? envs : style}
          fields={editing === 'characters' ? CHAR_FIELDS : editing === 'environments' ? ENV_FIELDS : STYLE_FIELDS}
          project={p}
          onClose={() => setEditing(null)}
          onSaved={(np) => { setProject(np); setEditing(null); }}
        />
      )}
    </div>
  );
}

function BibleCard({ icon, title, onEdit, children }: { icon: React.ReactNode; title: string; onEdit: () => void; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-line bg-subtle p-3">
      <div className="mb-2 flex items-center justify-between">
        <div className="flex items-center gap-2 text-[13px] font-semibold text-fg"><span className="text-accent">{icon}</span>{title}</div>
        <Button size="sm" variant="ghost" icon={<Pencil className="h-3.5 w-3.5" />} onClick={onEdit}>Sửa</Button>
      </div>
      <div className="space-y-2">{children}</div>
    </div>
  );
}

function BibleEditor({ kind, value, fields, project, onClose, onSaved }: {
  kind: 'characters' | 'environments' | 'style';
  value: Json;
  fields: [string, string][];
  project: Project;
  onClose: () => void;
  onSaved: (p: Project) => void;
}) {
  const toast = useToast();
  const isList = kind !== 'style';
  const [items, setItems] = useState<Json[]>(() => (isList ? (value as Json[]).map((x) => ({ ...x })) : [{ ...(value as Json) }]));
  const [overwrite, setOverwrite] = useState(false);
  const [saving, setSaving] = useState(false);

  const set = (i: number, k: string, v: string) => setItems((list) => list.map((x, j) => (j === i ? { ...x, [k]: v } : x)));
  const save = async () => {
    setSaving(true);
    try {
      const clean = items.map((x) => (isList ? { ...x, scenes: String(x.scenes ?? '').split(/[,\s]+/).map(Number).filter((n) => n > 0) } : x));
      const body = kind === 'characters' ? { characters: clean } : kind === 'environments' ? { environments: clean } : { style: clean[0] };
      onSaved(await aiStudio.bibles(project.id, { ...body, overwriteEdited: overwrite }));
      toast.success('Đã lưu', 'Prompt của các cảnh đã được viết lại theo bible mới.');
    } catch (e) {
      toast.error('Không lưu được', (e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  const prefix = kind === 'characters' ? 'C' : 'E';
  return (
    <Modal open onClose={onClose} size="lg" title={kind === 'characters' ? 'Sửa Character Bible' : kind === 'environments' ? 'Sửa Environment Bible' : 'Sửa Visual Style Bible'}
      description="Viết bằng tiếng Anh (Seedance hiểu tốt nhất). Mọi prompt dùng lại các mô tả này để giữ nhân vật / bối cảnh nhất quán giữa các cảnh."
      footer={<>
        <label className="mr-auto flex items-center gap-2 text-xs text-fg-muted"><input type="checkbox" checked={overwrite} onChange={(e) => setOverwrite(e.target.checked)} />Viết lại cả prompt tôi đã tự sửa</label>
        <Button variant="ghost" onClick={onClose}>Huỷ</Button>
        <Button variant="primary" loading={saving} onClick={save}>Lưu & cập nhật prompt</Button>
      </>}>
      <div className="max-h-[60vh] space-y-4 overflow-y-auto pr-1">
        {items.map((item, i) => (
          <div key={i} className="space-y-2 rounded-lg border border-line p-3">
            {isList && (
              <div className="flex items-center gap-2">
                <Input className="w-20" value={item.id ?? ''} onChange={(e) => set(i, 'id', e.target.value)} />
                <Input className="w-40" placeholder="Cảnh (1, 2…)" value={Array.isArray(item.scenes) ? item.scenes.join(', ') : item.scenes ?? ''} onChange={(e) => set(i, 'scenes', e.target.value)} />
                <Button size="sm" variant="danger-soft" className="ml-auto" onClick={() => setItems((l) => l.filter((_, j) => j !== i))}>Bỏ</Button>
              </div>
            )}
            {fields.map(([k, label]) => (
              <Field key={k} label={label}>
                <Textarea rows={k === 'consistency' || k === 'appearance' || k === 'direction' ? 2 : 1} value={item[k] ?? ''} onChange={(e) => set(i, k, e.target.value)} />
              </Field>
            ))}
          </div>
        ))}
        {isList && <Button size="sm" onClick={() => setItems((l) => [...l, { id: `${prefix}${l.length + 1}`, scenes: [] }])}>+ Thêm</Button>}
      </div>
    </Modal>
  );
}

