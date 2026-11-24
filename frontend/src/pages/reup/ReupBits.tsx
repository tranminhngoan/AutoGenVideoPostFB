import { useRef, useState, type ReactNode } from 'react';
import { UploadCloud } from 'lucide-react';
import { Badge, ProgressBar, cn } from '../../components/ui';
import type { Tone } from '../../lib/format';
import type { JobStatus, ReupJob } from '../../services/reup';

const STATUS: Record<JobStatus, { label: string; tone: Tone }> = {
  Draft: { label: 'Nháp', tone: 'neutral' },
  Queued: { label: 'Chờ máy tính', tone: 'warning' },
  Processing: { label: 'Đang xử lý', tone: 'info' },
  Done: { label: 'Hoàn tất', tone: 'success' },
  Failed: { label: 'Lỗi', tone: 'danger' },
  Cancelled: { label: 'Đã huỷ', tone: 'neutral' },
};

export function JobStatusBadge({ job }: { job: Pick<ReupJob, 'status' | 'previewRun'> }) {
  const s = STATUS[job.status] ?? { label: job.status, tone: 'neutral' as Tone };
  const active = job.status === 'Queued' || job.status === 'Processing';
  return (
    <Badge tone={s.tone} dot={active} pulse={active}>
      {s.label}
      {active && job.previewRun ? ' (xem thử)' : ''}
    </Badge>
  );
}

export function seconds(s?: number | null) {
  if (s == null || isNaN(s)) return '—';
  if (s < 60) return `${s.toFixed(s < 10 ? 1 : 0)}s`;
  return `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, '0')}`;
}

export function minutesText(sec: number) {
  if (!isFinite(sec) || sec <= 0) return '—';
  if (sec < 90) return `~${Math.max(1, Math.round(sec))} giây`;
  return `~${Math.round(sec / 60)} phút`;
}

/** Drag-and-drop / click file picker with an upload bar. */
export function Dropzone({
  accept,
  label,
  hint,
  onFile,
  progress,
  disabled,
  className,
}: {
  accept: string;
  label: ReactNode;
  hint?: ReactNode;
  onFile: (f: File) => void;
  progress?: number | null;
  disabled?: boolean;
  className?: string;
}) {
  const input = useRef<HTMLInputElement>(null);
  const [over, setOver] = useState(false);
  const busy = progress != null;
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => !busy && !disabled && input.current?.click()}
      onKeyDown={(e) => e.key === 'Enter' && !busy && !disabled && input.current?.click()}
      onDragOver={(e) => {
        e.preventDefault();
        setOver(true);
      }}
      onDragLeave={() => setOver(false)}
      onDrop={(e) => {
        e.preventDefault();
        setOver(false);
        const f = e.dataTransfer.files?.[0];
        if (f && !busy && !disabled) onFile(f);
      }}
      className={cn(
        'flex cursor-pointer flex-col items-center justify-center gap-2 rounded-xl border border-dashed px-6 py-8 text-center transition-colors',
        over ? 'border-accent bg-accent/[0.06]' : 'border-line-strong bg-subtle hover:border-accent/60 hover:bg-accent/[0.03]',
        (busy || disabled) && 'cursor-default opacity-80',
        className
      )}
    >
      <UploadCloud className="h-6 w-6 text-accent" />
      <div className="text-sm font-medium text-fg">{busy ? `Đang tải lên… ${Math.round((progress ?? 0) * 100)}%` : label}</div>
      {hint && !busy && <div className="max-w-md text-xs text-fg-muted">{hint}</div>}
      {busy && <ProgressBar value={(progress ?? 0) * 100} className="mt-1 max-w-xs" />}
      <input
        ref={input}
        type="file"
        accept={accept}
        className="hidden"
        onChange={(e) => {
          const f = e.target.files?.[0];
          e.target.value = '';
          if (f) onFile(f);
        }}
      />
    </div>
  );
}

export function Slider({
  label,
  value,
  min,
  max,
  step,
  onChange,
  format,
  hint,
  disabled,
}: {
  label: ReactNode;
  value: number;
  min: number;
  max: number;
  step: number;
  onChange: (v: number) => void;
  format?: (v: number) => string;
  hint?: ReactNode;
  disabled?: boolean;
}) {
  return (
    <label className={cn('block', disabled && 'pointer-events-none opacity-60')}>
      <span className="mb-1 flex items-baseline justify-between gap-2 text-[13px]">
        <span className="font-medium text-fg">{label}</span>
        <span className="tabular text-fg-muted">{format ? format(value) : value}</span>
      </span>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        className="w-full accent-[rgb(var(--accent))]"
      />
      {hint && <span className="mt-0.5 block text-xs text-fg-subtle">{hint}</span>}
    </label>
  );
}

export function SectionTitle({ n, title, description, aside }: { n: number; title: ReactNode; description?: ReactNode; aside?: ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-3">
      <div className="flex items-start gap-3">
        <span className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-indigo-500 to-violet-500 text-xs font-semibold text-white">
          {n}
        </span>
        <div>
          <h3 className="text-[15px] font-semibold text-fg">{title}</h3>
          {description && <p className="mt-0.5 text-[13px] text-fg-muted">{description}</p>}
        </div>
      </div>
      {aside}
    </div>
  );
}

export function Stat({ label, value, hint, tone }: { label: string; value: ReactNode; hint?: ReactNode; tone?: Tone }) {
  const color: Record<Tone, string> = {
    neutral: 'text-fg',
    info: 'text-accent',
    success: 'text-success',
    warning: 'text-warning',
    danger: 'text-danger',
  };
  return (
    <div className="rounded-lg border border-line bg-surface px-4 py-3 shadow-card">
      <div className="text-xs text-fg-muted">{label}</div>
      <div className={cn('mt-1 text-2xl font-semibold tabular tracking-tight', color[tone ?? 'neutral'])}>{value}</div>
      {hint && <div className="mt-0.5 text-xs text-fg-subtle">{hint}</div>}
    </div>
  );
}
