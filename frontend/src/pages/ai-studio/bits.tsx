import { createContext, useContext, type ReactNode } from 'react';
import { Badge, cn } from '../../components/ui';
import type { Tone } from '../../lib/format';
import type { GenStatus, Overview, ProjectStatus } from '../../services/aiStudio';

export interface StudioCtx {
  overview: Overview | null;
  refreshOverview: () => void;
  onKeyNeeded: () => void;
}

export const AiStudioContext = createContext<StudioCtx>({ overview: null, refreshOverview: () => {}, onKeyNeeded: () => {} });
export const useAiStudio = () => useContext(AiStudioContext);

const PROJECT_STATUS: Record<ProjectStatus, { label: string; tone: Tone; pulse?: boolean }> = {
  Draft: { label: 'Chưa phân tích', tone: 'neutral' },
  Queued: { label: 'Chờ máy tính', tone: 'info', pulse: true },
  Analyzing: { label: 'Đang phân tích', tone: 'info', pulse: true },
  Analyzed: { label: 'Đã phân tích', tone: 'success' },
  Failed: { label: 'Lỗi', tone: 'danger' },
  Cancelled: { label: 'Đã huỷ', tone: 'warning' },
};

const GEN_STATUS: Record<GenStatus, { label: string; tone: Tone; pulse?: boolean }> = {
  Queued: { label: 'Chờ gửi', tone: 'info', pulse: true },
  Generating: { label: 'Seedance đang tạo', tone: 'info', pulse: true },
  Recovering: { label: 'Đang lấy lại', tone: 'info', pulse: true },
  Done: { label: 'Xong', tone: 'success' },
  Failed: { label: 'Lỗi', tone: 'danger' },
  Cancelled: { label: 'Đã huỷ', tone: 'neutral' },
};

export function ProjectStatusBadge({ status }: { status: ProjectStatus }) {
  const m = PROJECT_STATUS[status] ?? PROJECT_STATUS.Draft;
  return (
    <Badge tone={m.tone} dot pulse={m.pulse}>
      {m.label}
    </Badge>
  );
}

export function GenStatusBadge({ status }: { status: GenStatus }) {
  const m = GEN_STATUS[status] ?? GEN_STATUS.Queued;
  return (
    <Badge tone={m.tone} dot pulse={m.pulse}>
      {m.label}
    </Badge>
  );
}

export const sec = (s?: number | null) => {
  if (s == null || isNaN(s)) return '—';
  const m = Math.floor(s / 60);
  const r = s - m * 60;
  return `${m}:${r < 10 ? '0' : ''}${r.toFixed(r % 1 ? 1 : 0)}`;
};

export const usd = (n?: number | null, digits = 3) => `$${(n ?? 0).toFixed(digits)}`;

/** A section of the workspace: numbered step, title, description, content. */
export function Step({ id, n, title, description, actions, children, dim }: {
  id: string;
  n: number;
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  dim?: boolean;
}) {
  return (
    <section id={id} className={cn('scroll-mt-20 rounded-2xl border border-line bg-surface shadow-card', dim && 'opacity-60')}>
      <header className="flex flex-wrap items-start justify-between gap-3 border-b border-line px-5 py-4">
        <div className="flex min-w-0 items-start gap-3">
          <span className="mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-full bg-gradient-to-br from-accent to-fuchsia-500 text-xs font-semibold text-white shadow-sm">
            {n}
          </span>
          <div className="min-w-0">
            <h2 className="text-[15px] font-semibold text-fg">{title}</h2>
            {description && <p className="mt-0.5 text-[13px] text-fg-muted">{description}</p>}
          </div>
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </header>
      <div className="px-5 py-4">{children}</div>
    </section>
  );
}

/** 0–1 confidence as a short bar with a colour. */
export function ConfidenceMeter({ value, className }: { value: number; className?: string }) {
  const pct = Math.round(Math.max(0, Math.min(1, value)) * 100);
  const tone = value >= 0.75 ? 'bg-success' : value >= 0.5 ? 'bg-warning' : 'bg-danger';
  return (
    <span className={cn('inline-flex items-center gap-1.5 text-xs text-fg-muted', className)} title="Độ tin cậy của phần phân tích cảnh này">
      <span className="h-1.5 w-14 overflow-hidden rounded-full bg-muted">
        <span className={cn('block h-full rounded-full', tone)} style={{ width: `${pct}%` }} />
      </span>
      {pct}%
    </span>
  );
}

export function Chip({ children, className, title }: { children: ReactNode; className?: string; title?: string }) {
  return (
    <span title={title} className={cn('inline-flex items-center gap-1 rounded-md border border-line bg-subtle px-1.5 py-0.5 text-[11px] text-fg-muted', className)}>
      {children}
    </span>
  );
}

export function Swatches({ colors }: { colors: string[] }) {
  return (
    <span className="inline-flex overflow-hidden rounded-md border border-line">
      {colors.slice(0, 6).map((c) => (
        <span key={c} className="h-4 w-4" style={{ background: c }} title={c} />
      ))}
    </span>
  );
}

/** "uncertain" and empty values are hidden everywhere: the page only shows what the analysis actually saw. */
export const fact = (v: unknown): string | null => {
  if (v == null) return null;
  const s = Array.isArray(v) ? v.filter((x) => fact(x)).join(', ') : typeof v === 'object' ? null : String(v).trim();
  if (!s) return null;
  const l = s.toLowerCase();
  return l === 'uncertain' || l.startsWith('uncertain') || l === 'none' || l === 'n/a' || l === 'unknown' ? null : s;
};

export const CAMERA_VI: Record<string, string> = {
  'static (locked-off)': 'Máy đứng yên',
  'handheld (small natural shake)': 'Cầm tay, rung nhẹ',
  'pan left': 'Lia trái',
  'pan right': 'Lia phải',
  'tilt up': 'Ngước lên',
  'tilt down': 'Chúc xuống',
  'push-in (zoom in / dolly in)': 'Tiến vào / zoom vào',
  'pull-out (zoom out / dolly out)': 'Lùi ra / zoom ra',
};

export const MODE_INFO: Record<string, { label: string; short: string; description: string }> = {
  Faithful: {
    label: 'A · Bám sát',
    short: 'Bám sát',
    description: 'Giữ chủ đề, hành động, trình tự, cảm xúc, nhịp và bố cục — viết lại bằng mô tả mới, không chép tài sản có bản quyền.',
  },
  Creative: {
    label: 'B · Biến tấu',
    short: 'Biến tấu',
    description: 'Giữ ý tưởng và hành động chính, thay có chủ đích nhân vật / trang phục / bối cảnh / góc quay / ánh sáng.',
  },
  Cinematic: {
    label: 'C · Điện ảnh',
    short: 'Điện ảnh',
    description: 'Giữ nội dung, nâng cấp ngôn ngữ máy quay, bố cục, ánh sáng, màu, độ sâu và độ chân thực.',
  },
};

/** The channel theme: added to every prompt after the analysis (who the people look like, setting, grade). */
export function TopicPicker({ topics, value, onChange }: { topics: { id: string; label: string; description: string }[]; value: string; onChange: (v: string) => void }) {
  const all = [{ id: '', label: 'Không chủ đề', description: 'Chỉ theo đúng những gì AI thấy trong video.' }, ...topics];
  return (
    <div>
      <div className="mb-1.5 text-[13px] font-medium text-fg">Chủ đề kênh</div>
      <div className="grid gap-2">
        {all.map((t) => (
          <button key={t.id} type="button" onClick={() => onChange(t.id)}
            className={cn('rounded-lg border px-3 py-2 text-left transition', value === t.id ? 'border-accent bg-accent/[0.06] ring-1 ring-accent/40' : 'border-line hover:bg-subtle')}>
            <div className="text-[13px] font-medium text-fg">{t.label}</div>
            <div className="text-[11px] text-fg-muted">{t.description}</div>
          </button>
        ))}
      </div>
    </div>
  );
}
