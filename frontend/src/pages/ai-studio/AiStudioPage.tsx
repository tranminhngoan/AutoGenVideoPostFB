import { useCallback, useEffect, useRef, useState } from 'react';
import { Cpu, Film, FolderOpen, Gauge, History, Settings2, Sparkles, UploadCloud, Wallet } from 'lucide-react';
import { Alert, Button, Card, EmptyState, Field, Input, ProgressBar, Skeleton, Thumb, cn } from '../../components/ui';
import { StudioUnlock } from '../../components/studio/StudioUnlock';
import { useToast } from '../../components/feedback';
import { href, navigate } from '../../lib/router';
import { formatBytes, timeAgo } from '../../lib/format';
import { StudioError } from '../../services/studio';
import { aiStudio, type Overview, type ProjectSummary } from '../../services/aiStudio';
import { AiStudioContext, ProjectStatusBadge, TopicPicker, sec, usd, useAiStudio } from './bits';
import { Workspace } from './Workspace';
import { HistoryTable } from './Generator';
import { SettingsView } from './SettingsView';

const ACCEPT = '.mp4,.mov,.webm,video/mp4,video/quicktime,video/webm';
const MAX_BYTES = 500 * 1024 * 1024;

export function AiStudioPage({ segments }: { segments: string[] }) {
  const [overview, setOverview] = useState<Overview | null>(null);
  const [locked, setLocked] = useState(false);
  const [first, second] = segments;
  const view = first === 'p' && second ? 'project' : first === 'history' ? 'history' : first === 'settings' ? 'settings' : 'home';

  const refreshOverview = useCallback(async () => {
    try {
      setOverview(await aiStudio.overview());
      setLocked(false);
    } catch (e) {
      if (e instanceof StudioError && e.needsKey) setLocked(true);
    }
  }, []);

  useEffect(() => {
    refreshOverview();
    const t = setInterval(refreshOverview, 20000);
    return () => clearInterval(t);
  }, [refreshOverview]);

  useEffect(() => {
    document.title = 'AI Video Studio · Facebook chéo uy tính';
  }, []);

  const tabs = [
    { id: 'home', path: '/ai-studio', label: 'Dự án', icon: FolderOpen },
    { id: 'history', path: '/ai-studio/history', label: 'Lịch sử tạo video', icon: History },
    { id: 'settings', path: '/ai-studio/settings', label: 'Máy phân tích', icon: Settings2 },
  ];

  return (
    <AiStudioContext.Provider value={{ overview, refreshOverview, onKeyNeeded: () => setLocked(true) }}>
      <div className="space-y-5">
        <Hero overview={overview} />
        <nav className="flex flex-wrap gap-1 rounded-xl border border-line bg-surface p-1 shadow-card">
          {tabs.map((t) => {
            const Icon = t.icon;
            const active = view === t.id || (view === 'project' && t.id === 'home');
            return (
              <a
                key={t.id}
                href={href(t.path)}
                className={cn(
                  'inline-flex h-8 items-center gap-2 rounded-lg px-3 text-[13px] transition-colors',
                  active ? 'bg-fg text-canvas' : 'text-fg-muted hover:bg-muted hover:text-fg'
                )}
              >
                <Icon className="h-4 w-4" /> {t.label}
              </a>
            );
          })}
        </nav>

        {locked ? (
          <StudioUnlock onUnlocked={() => { setLocked(false); refreshOverview(); }} />
        ) : view === 'project' ? (
          <Workspace key={second} projectId={second!} />
        ) : view === 'history' ? (
          <Card className="p-0">
            <HistoryTable />
          </Card>
        ) : view === 'settings' ? (
          <SettingsView />
        ) : (
          <Home />
        )}
      </div>
    </AiStudioContext.Provider>
  );
}

function Hero({ overview: o }: { overview: Overview | null }) {
  const pc = !o ? null : o.pcOnline ? (o.engine.ready ? 'ready' : 'noengine') : o.pcUpdating ? 'updating' : 'off';
  return (
    <div className="relative overflow-hidden rounded-2xl border border-line bg-surface shadow-card">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(60%_120%_at_0%_0%,rgb(var(--accent)/0.18),transparent_60%),radial-gradient(50%_120%_at_100%_0%,rgba(217,70,239,0.14),transparent_60%)]" />
      <div className="pointer-events-none absolute inset-0 opacity-[0.35] [background-image:linear-gradient(rgb(var(--line))_1px,transparent_1px),linear-gradient(90deg,rgb(var(--line))_1px,transparent_1px)] [background-size:28px_28px] [mask-image:linear-gradient(to_bottom,black,transparent)]" />
      <div className="relative flex flex-col gap-4 p-5 sm:flex-row sm:items-end sm:justify-between sm:p-6">
        <div className="max-w-2xl">
          <div className="inline-flex items-center gap-1.5 rounded-full border border-line bg-surface/80 px-2.5 py-1 text-[11px] font-medium text-fg-muted backdrop-blur">
            <Sparkles className="h-3.5 w-3.5 text-accent" /> Video → phân tích → prompt → Veo
          </div>
          <h1 className="mt-3 text-2xl font-semibold tracking-tight text-fg sm:text-[28px]">AI Video Studio</h1>
          <p className="mt-1.5 text-[13px] leading-relaxed text-fg-muted sm:text-sm">
            Tải video tham chiếu lên, AI trên máy tính của bạn (miễn phí, không gửi video ra ngoài) đọc từng cảnh, hành động, máy quay, ánh sáng, màu —
            rồi viết prompt điện ảnh để Google Veo tạo video mới ngay tại đây.
          </p>
        </div>
        <div className="grid grid-cols-2 gap-2 sm:w-[360px]">
          <Pill icon={<Cpu className="h-4 w-4" />} label="Máy phân tích"
            value={pc == null ? '…' : pc === 'ready' ? 'Sẵn sàng' : pc === 'noengine' ? 'Chưa cài' : pc === 'updating' ? 'Đang cập nhật' : 'Đang tắt'}
            tone={pc === 'ready' ? 'text-success' : pc === 'off' || pc === 'noengine' ? 'text-warning' : 'text-fg'} />
          <Pill icon={<Gauge className="h-4 w-4" />} label="Tốc độ model 4B"
            value={o?.engine.tokensPerSecond?.['4b'] ? `${o.engine.tokensPerSecond['4b']} token/s` : '—'} />
          <Pill icon={<Wallet className="h-4 w-4" />} label="Đã chi tháng này"
            value={o ? `${usd(o.budget.monthSpentUsd, 2)}${o.budget.monthlyBudgetUsd > 0 ? ` / ${usd(o.budget.monthlyBudgetUsd, 0)}` : ''}` : '—'} />
          <Pill icon={<Film className="h-4 w-4" />} label="Veo" value={o ? (o.seedanceConfigured ? 'Đã kết nối' : 'Chưa có khoá') : '…'}
            tone={o && !o.seedanceConfigured ? 'text-warning' : 'text-fg'} />
        </div>
      </div>
    </div>
  );
}

function Pill({ icon, label, value, tone = 'text-fg' }: { icon: React.ReactNode; label: string; value: string; tone?: string }) {
  return (
    <div className="rounded-xl border border-line bg-surface/80 px-3 py-2 backdrop-blur">
      <div className="flex items-center gap-1.5 text-[11px] text-fg-subtle">{icon}{label}</div>
      <div className={cn('mt-0.5 truncate text-[13px] font-semibold', tone)}>{value}</div>
    </div>
  );
}

function Home() {
  const [projects, setProjects] = useState<ProjectSummary[] | null>(null);
  const { overview, onKeyNeeded } = useAiStudio();

  const load = useCallback(async () => {
    try {
      setProjects(await aiStudio.projects());
    } catch (e) {
      if (e instanceof StudioError && e.needsKey) onKeyNeeded();
    }
  }, [onKeyNeeded]);

  useEffect(() => {
    load();
    const t = setInterval(load, 10000);
    return () => clearInterval(t);
  }, [load]);

  return (
    <div className="grid gap-5 lg:grid-cols-[380px_1fr]">
      <div className="space-y-4">
        <UploadCard />
        {overview && !overview.pcOnline && (
          <Alert tone="warning" title={overview.pcUpdating ? 'Máy tính đang cập nhật bản mới' : 'Máy tính đang tắt'}>
            Phân tích chạy trên máy tính của bạn (miễn phí). Bật máy và chạy <code>scripts\pc\start-pc-worker.bat</code> — video đã tải lên sẽ chờ đến lúc đó.
          </Alert>
        )}
        {overview && overview.pcOnline && !overview.engine.ready && (
          <Alert tone="danger" title="Máy tính chưa cài bộ phân tích">
            {overview.engine.error ?? ''} Chạy <code>scripts\pc\setup-ai-studio.bat</code> một lần.
          </Alert>
        )}
      </div>
      <div>
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-[15px] font-semibold text-fg">Dự án</h2>
          {projects && <span className="text-xs text-fg-subtle">{projects.length} dự án</span>}
        </div>
        {!projects ? (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-56" />)}</div>
        ) : projects.length === 0 ? (
          <Card><EmptyState icon={<Film />} title="Chưa có dự án" description="Tải một video tham chiếu lên ở khung bên trái để bắt đầu." /></Card>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {projects.map((p) => (
              <a key={p.id} href={href(`/ai-studio/p/${p.id}`)} className="group overflow-hidden rounded-xl border border-line bg-surface shadow-card transition hover:-translate-y-0.5 hover:shadow-pop">
                <div className="relative">
                  <Thumb src={p.hasThumb ? aiStudio.thumbUrl(p.id) : null} className="aspect-video w-full" />
                  <div className="absolute left-2 top-2 rounded-full bg-surface/90 shadow-sm backdrop-blur"><ProjectStatusBadge status={p.status} /></div>
                  <span className="absolute bottom-2 right-2 rounded bg-black/60 px-1.5 py-0.5 text-[11px] font-medium text-white">{sec(Math.round(p.durationSeconds))}</span>
                </div>
                <div className="space-y-1 p-3">
                  <div className="truncate text-sm font-medium text-fg group-hover:text-accent">{p.title}</div>
                  <div className="flex flex-wrap gap-x-3 text-xs text-fg-muted">
                    <span>{p.width}×{p.height}</span>
                    {p.scenesTotal > 0 && <span>{p.scenesTotal} cảnh</span>}
                    {p.clipsDone > 0 && <span>{p.clipsDone} clip · {usd(p.costUsd)}</span>}
                    <span>{timeAgo(p.createdAtUtc)}</span>
                  </div>
                  {(p.status === 'Analyzing' || p.status === 'Queued') && (
                    <div className="pt-1">
                      <ProgressBar value={p.progressPercent} />
                      <div className="mt-1 truncate text-[11px] text-fg-subtle">{p.progressText}</div>
                    </div>
                  )}
                </div>
              </a>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

interface Picked {
  file: File;
  url: string;
  duration?: number;
  width?: number;
  height?: number;
  error?: string;
}

function UploadCard() {
  const toast = useToast();
  const { onKeyNeeded, overview } = useAiStudio();
  const input = useRef<HTMLInputElement>(null);
  const [topic, setTopic] = useState(() => localStorage.getItem('aiStudio.topic') ?? '');
  const [picked, setPicked] = useState<Picked | null>(null);
  const [title, setTitle] = useState('');
  const [progress, setProgress] = useState<number | null>(null);
  const [drag, setDrag] = useState(false);

  useEffect(() => () => { if (picked) URL.revokeObjectURL(picked.url); }, [picked]);

  const pick = (file?: File | null) => {
    if (!file) return;
    const ext = file.name.toLowerCase().split('.').pop() ?? '';
    const p: Picked = { file, url: URL.createObjectURL(file) };
    if (!['mp4', 'mov', 'webm', 'm4v'].includes(ext)) p.error = 'Chỉ nhận MP4, MOV hoặc WEBM.';
    else if (file.size > MAX_BYTES) p.error = `File ${formatBytes(file.size)} — tối đa 500 MB.`;
    setPicked(p);
    if (!title) setTitle(file.name.replace(/\.[^.]+$/, ''));
  };

  const start = async () => {
    if (!picked || picked.error) return;
    setProgress(0);
    try {
      localStorage.setItem('aiStudio.topic', topic);
      const p = await aiStudio.createProject(picked.file, title.trim(), topic, setProgress);
      toast.success('Đã tải lên', 'Bấm "Phân tích video" để AI trên máy tính đọc video.');
      navigate(`/ai-studio/p/${p.id}`);
    } catch (e) {
      if (e instanceof StudioError && e.needsKey) onKeyNeeded();
      toast.error('Không tải lên được', (e as Error).message);
      setProgress(null);
    }
  };

  return (
    <Card className="overflow-hidden p-0">
      <div className="border-b border-line px-4 py-3">
        <div className="text-[15px] font-semibold text-fg">Video tham chiếu mới</div>
        <div className="text-xs text-fg-muted">MP4 / MOV / WEBM · tối đa 500 MB · 2 giây – 3 phút</div>
      </div>
      <div className="space-y-3 p-4">
        {!picked ? (
          <button
            type="button"
            onClick={() => input.current?.click()}
            onDragOver={(e) => { e.preventDefault(); setDrag(true); }}
            onDragLeave={() => setDrag(false)}
            onDrop={(e) => { e.preventDefault(); setDrag(false); pick(e.dataTransfer.files?.[0]); }}
            className={cn(
              'flex w-full flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed px-4 py-10 text-center transition',
              drag ? 'border-accent bg-accent/5' : 'border-line-strong hover:border-accent/60 hover:bg-subtle'
            )}
          >
            <span className="grid h-11 w-11 place-items-center rounded-full bg-gradient-to-br from-accent to-fuchsia-500 text-white shadow"><UploadCloud className="h-5 w-5" /></span>
            <span className="text-sm font-medium text-fg">Kéo thả video vào đây</span>
            <span className="text-xs text-fg-muted">hoặc bấm để chọn file</span>
          </button>
        ) : (
          <div className="space-y-2">
            <video
              src={picked.url}
              controls
              playsInline
              className="max-h-72 w-full rounded-lg bg-black"
              onLoadedMetadata={(e) => {
                const v = e.currentTarget;
                setPicked((p) => (p ? { ...p, duration: v.duration, width: v.videoWidth, height: v.videoHeight } : p));
              }}
              onError={() => setPicked((p) => (p ? { ...p, error: p.error ?? 'Trình duyệt không đọc được file này (codec lạ?) — server vẫn sẽ kiểm tra lại khi tải lên.' } : p))}
            />
            <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-fg-muted">
              <span className="max-w-full truncate font-medium text-fg">{picked.file.name}</span>
              <span>{formatBytes(picked.file.size)}</span>
              {picked.duration != null && isFinite(picked.duration) && <span>{sec(picked.duration)}</span>}
              {picked.width ? <span>{picked.width}×{picked.height}</span> : null}
            </div>
            {picked.error && <p className="text-xs text-danger">{picked.error}</p>}
            {picked.duration != null && isFinite(picked.duration) && picked.duration > 180 && <p className="text-xs text-danger">Video dài hơn 3 phút — cắt bớt trước khi tải lên.</p>}
          </div>
        )}
        <input ref={input} type="file" accept={ACCEPT} hidden onChange={(e) => { pick(e.target.files?.[0]); e.target.value = ''; }} />
        {picked && (
          <>
            <Field label="Tên dự án">
              <Input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={300} />
            </Field>
            <TopicPicker topics={overview?.topics ?? []} value={topic} onChange={setTopic} />
            {progress != null && <ProgressBar value={progress * 100} />}
            <div className="flex gap-2">
              <Button variant="ghost" onClick={() => { setPicked(null); setTitle(''); }} disabled={progress != null}>Đổi file</Button>
              <Button variant="accent" className="flex-1" icon={<UploadCloud className="h-4 w-4" />} loading={progress != null}
                disabled={!!picked.error || (picked.duration != null && picked.duration > 180)} onClick={start}>
                {progress != null ? `Đang tải lên ${Math.round(progress * 100)}%` : 'Tải lên'}
              </Button>
            </div>
          </>
        )}
        <p className="text-[11px] leading-relaxed text-fg-subtle">
          Chỉ dùng video bạn sở hữu hoặc được phép sử dụng. Video tham chiếu không được gửi tới dịch vụ AI bên ngoài — chỉ máy tính của bạn đọc nó;
          prompt tạo lại là mô tả mới, nhưng không tự động xoá rủi ro bản quyền.
        </p>
      </div>
    </Card>
  );
}
