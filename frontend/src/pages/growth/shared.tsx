import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { AlertTriangle, Clapperboard, Image as ImageIcon, Images, MessageCircleQuestion, CircleDashed, RefreshCw } from 'lucide-react';
import { Alert, Badge, Button, Skeleton, cn } from '../../components/ui';
import { StudioError } from '../../services/studio';
import type { Campaign, Content, GrowthPage } from '../../services/growth';
import { CAMPAIGN_STATUS, KIND_LABEL, STATUS_LABEL, STATUS_TONE } from '../../lib/growthMath';

/** What every sub-page gets from the shell. */
export interface GrowthCtx {
  pages: GrowthPage[];
  page: GrowthPage | null;
  campaigns: Campaign[];
  campaign: Campaign | null;
  selectPage: (id: string) => void;
  selectCampaign: (id: string | null) => void;
  reloadCampaigns: () => Promise<void>;
  /** Replace the selected campaign after an action returned it. */
  setCampaign: (c: Campaign) => void;
  onNeedKey: () => void;
}

/** Loads data with loading / error state; re-runs when deps change. A 401 asks for the studio key. */
export function useLoad<T>(fn: () => Promise<T>, deps: unknown[], onNeedKey?: () => void) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const seq = useRef(0);
  const run = useCallback(async () => {
    const id = ++seq.current;
    setLoading(true);
    setError(null);
    try {
      const d = await fn();
      if (id === seq.current) setData(d);
    } catch (e) {
      if (id !== seq.current) return;
      if (e instanceof StudioError && e.needsKey) onNeedKey?.();
      setError((e as Error).message);
    } finally {
      if (id === seq.current) setLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
  useEffect(() => {
    run();
  }, [run]);
  return { data, error, loading, reload: run, setData };
}

export function LoadError({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <Alert
      tone="danger"
      icon={<AlertTriangle className="h-4 w-4" />}
      title="Không tải được dữ liệu"
      action={onRetry && <Button size="sm" variant="secondary" icon={<RefreshCw className="h-3.5 w-3.5" />} onClick={onRetry}>Thử lại</Button>}
    >
      {message}
    </Alert>
  );
}

export function LoadingBlock({ rows = 3 }: { rows?: number }) {
  return (
    <div className="space-y-3" aria-busy="true" aria-label="Đang tải">
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} className={cn('h-20 w-full', i === 0 && 'h-28')} />
      ))}
    </div>
  );
}

export function CampaignBadge({ status }: { status: string }) {
  const s = CAMPAIGN_STATUS[status] ?? { label: status, tone: 'neutral' as const };
  return <Badge tone={s.tone} dot pulse={status === 'Running'}>{s.label}</Badge>;
}

export function ContentBadge({ status }: { status: string }) {
  return <Badge tone={STATUS_TONE[status] ?? 'neutral'}>{STATUS_LABEL[status] ?? status}</Badge>;
}

export function KindIcon({ kind, className = 'h-3.5 w-3.5' }: { kind: string; className?: string }) {
  switch (kind) {
    case 'Reel':
      return <Clapperboard className={className} />;
    case 'Photo':
      return <ImageIcon className={className} />;
    case 'Album':
      return <Images className={className} />;
    case 'Story':
      return <CircleDashed className={className} />;
    default:
      return <MessageCircleQuestion className={className} />;
  }
}

export function KindLabel({ kind }: { kind: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-fg-muted">
      <KindIcon kind={kind} /> {KIND_LABEL[kind] ?? kind}
    </span>
  );
}

export function PageAvatar({ page, size = 40 }: { page: { pictureUrl?: string; pageName: string } | null | undefined; size?: number }) {
  const [broken, setBroken] = useState(false);
  const initials = (page?.pageName ?? '?').split(/\s+/).map((w) => w[0]).join('').slice(0, 2).toUpperCase();
  return page?.pictureUrl && !broken ? (
    <img src={page.pictureUrl} alt="" width={size} height={size} onError={() => setBroken(true)} className="shrink-0 rounded-full border border-line object-cover" style={{ width: size, height: size }} />
  ) : (
    <span className="flex shrink-0 items-center justify-center rounded-full bg-muted text-xs font-semibold text-fg-muted" style={{ width: size, height: size }}>
      {initials}
    </span>
  );
}

/** A short line for a content item: kind, title / caption, status. */
export function contentTitle(c: Pick<Content, 'title' | 'caption' | 'sourceTitle'>) {
  const t = c.title?.trim() || c.sourceTitle?.trim() || c.caption?.replace(/\s+/g, ' ').trim() || '(không tiêu đề)';
  return t.length > 70 ? t.slice(0, 70) + '…' : t;
}

export function Section({ title, description, actions, children, className }: { title: ReactNode; description?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={cn('min-w-0 rounded-lg border border-line bg-surface shadow-card', className)}>
      <div className="flex flex-wrap items-start justify-between gap-3 px-4 pt-4 sm:px-5">
        <div className="min-w-0">
          <h2 className="text-[15px] font-semibold text-fg">{title}</h2>
          {description && <p className="mt-0.5 text-[13px] text-fg-muted">{description}</p>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
      <div className="px-4 pb-4 pt-3 sm:px-5">{children}</div>
    </section>
  );
}

/** Shown when no campaign is selected. */
export function NoCampaign({ ctx, action }: { ctx: GrowthCtx; action?: ReactNode }) {
  return (
    <div className="rounded-lg border border-dashed border-line px-6 py-12 text-center">
      <div className="text-[15px] font-semibold text-fg">{ctx.page ? 'Page này chưa có chiến dịch' : 'Chưa chọn Page'}</div>
      <p className="mx-auto mt-1 max-w-md text-[13px] text-fg-muted">
        {ctx.page
          ? 'Tạo chiến dịch "Meta One 7-Day Growth Challenge" — 1 Reels, 2 bài ảnh, 3 Story mỗi ngày (tuỳ chỉnh được).'
          : 'Chọn một Page đã kết nối ở phía trên. Kết nối Page mới ở Cài đặt → Facebook.'}
      </p>
      {action && <div className="mt-4 flex justify-center">{action}</div>}
    </div>
  );
}
