import React from 'react';
import { AlertTriangle, CheckCircle2, ExternalLink, Film, Globe2, Newspaper, X } from 'lucide-react';
import { AutoPostPageDto, AutoPostRunDto, AutoPostTotalsDto } from '../services/api';
import { Badge, Button, ButtonLink, Card, ProgressBar, cn } from './ui';
import { href } from '../lib/router';
import { formatDateTime, Tone } from '../lib/format';

export const STATE_META: Record<string, { label: string; tone: Tone; pulse?: boolean }> = {
  Upcoming: { label: 'Sắp tới', tone: 'neutral' },
  Missed: { label: 'Đã qua', tone: 'neutral' },
  Producing: { label: 'Đang làm video', tone: 'info', pulse: true },
  WaitingSlot: { label: 'Chờ đến giờ đăng', tone: 'info' },
  Publishing: { label: 'Đang đăng', tone: 'info', pulse: true },
  Published: { label: 'Đã đăng', tone: 'success' },
  Failed: { label: 'Lỗi', tone: 'danger' },
  Skipped: { label: 'Đã huỷ', tone: 'neutral' },
};

export const isActiveRun = (run?: AutoPostRunDto | null) =>
  !!run && ['Producing', 'WaitingSlot', 'Publishing'].includes(run.status);

export function StateBadge({ state }: { state: string }) {
  const meta = STATE_META[state] ?? { label: state, tone: 'neutral' as Tone };
  return (
    <Badge tone={meta.tone} dot pulse={meta.pulse}>
      {meta.label}
    </Badge>
  );
}

export function RegionBadge({ region }: { region?: string | null }) {
  if (!region) return null;
  return region === 'World' ? (
    <Badge tone="info">
      <Globe2 className="h-3 w-3" /> Quốc tế
    </Badge>
  ) : (
    <Badge tone="warning">
      <Newspaper className="h-3 w-3" /> Việt Nam
    </Badge>
  );
}

/** First letters of the page name, for the avatar circle. */
function initials(name: string) {
  const words = name.trim().split(/\s+/).filter(Boolean);
  return (words.length > 1 ? words[0][0] + words[words.length - 1][0] : name.slice(0, 2)).toUpperCase();
}

export function PageAvatar({ name, enabled, className }: { name: string; enabled?: boolean; className?: string }) {
  return (
    <span
      className={cn(
        'flex shrink-0 items-center justify-center rounded-full text-xs font-semibold',
        enabled ? 'bg-accent/10 text-accent' : 'bg-muted text-fg-subtle',
        className ?? 'h-9 w-9'
      )}
    >
      {initials(name)}
    </span>
  );
}

function vnTime(utc?: string | null) {
  if (!utc) return null;
  const d = new Date(utc);
  if (isNaN(d.getTime())) return null;
  // Slots are defined in Vietnam time regardless of the viewer's time zone
  return new Date(d.getTime() + 7 * 3600 * 1000).toISOString().slice(11, 16);
}

/** Totals across every page: shown on the dashboard and the auto-post page. */
export function AutoPostTotals({ totals }: { totals: AutoPostTotalsDto }) {
  const tiles: { label: string; value: React.ReactNode; hint?: string; tone?: string }[] = [
    { label: 'Page đang tự đăng', value: `${totals.pagesEnabled}/${totals.pages}` },
    {
      label: 'Đã đăng hôm nay',
      value: `${totals.publishedToday}/${totals.plannedToday}`,
      hint: `${totals.publishedLast7Days} video trong 7 ngày`,
    },
    { label: 'Đang xử lý', value: totals.inProgress, tone: totals.inProgress ? 'text-accent' : undefined },
    { label: 'Lỗi hôm nay', value: totals.failedToday, tone: totals.failedToday ? 'text-danger' : undefined },
  ];
  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      {tiles.map((t) => (
        <Card key={t.label} className="px-4 py-3">
          <p className="text-xs text-fg-muted">{t.label}</p>
          <p className={cn('mt-1 text-2xl font-semibold tabular-nums text-fg', t.tone)}>{t.value}</p>
          {t.hint && <p className="mt-0.5 text-[11px] text-fg-subtle">{t.hint}</p>}
        </Card>
      ))}
    </div>
  );
}

/** Summary card of one page: topic, on/off, today's progress, what it is doing now. */
export function AutoPostPageCard({ page, onOpen }: { page: AutoPostPageDto; onOpen?: () => void }) {
  const s = page.settings;
  const planned = s.enabled ? s.postsPerDay : 0;
  const current = page.currentRun;
  const next = vnTime(page.nextSlotUtc);
  return (
    <Card className="flex flex-col p-4">
      <div className="flex items-start gap-3">
        <PageAvatar name={page.pageName} enabled={s.enabled} />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-semibold text-fg" title={page.pageName}>
            {page.pageName}
          </p>
          <div className="mt-1 flex flex-wrap items-center gap-1.5">
            <Badge tone="neutral">{page.topicName}</Badge>
            <Badge tone={s.enabled ? 'success' : 'neutral'} dot>
              {s.enabled ? 'Đang bật' : 'Đang tắt'}
            </Badge>
          </div>
        </div>
      </div>

      <div className="mt-3">
        <div className="mb-1 flex items-baseline justify-between text-xs">
          <span className="text-fg-muted">Hôm nay</span>
          <span className="tabular-nums text-fg">
            <b>{page.publishedToday}</b>/{planned || s.postsPerDay} video
            {page.failedToday > 0 && <span className="ml-1.5 text-danger">· {page.failedToday} lỗi</span>}
          </span>
        </div>
        <ProgressBar value={planned ? (page.publishedToday / planned) * 100 : 0} tone="success" />
      </div>

      <div className="mt-3 min-h-[40px] text-[13px]">
        {current ? (
          <div className="space-y-1">
            <div className="flex items-center gap-1.5">
              <StateBadge state={current.status} />
              <span className="text-xs text-fg-subtle">lượt {current.slotTime}</span>
            </div>
            {current.articleTitle && <p className="line-clamp-2 leading-snug text-fg">{current.articleTitle}</p>}
            {current.status === 'Producing' && current.progressPercentage != null && (
              <ProgressBar value={current.progressPercentage} className="max-w-[220px]" />
            )}
          </div>
        ) : s.enabled && next ? (
          <p className="text-fg-muted">
            Lượt tiếp theo <b className="font-mono text-fg">{next}</b>
          </p>
        ) : (
          <p className="text-fg-subtle">{s.enabled ? 'Hết lượt hôm nay' : 'Chưa bật tự động đăng'}</p>
        )}
      </div>

      {page.lastPublished && (
        <p className="mt-2 truncate text-xs text-fg-subtle">
          <CheckCircle2 className="mr-1 inline h-3.5 w-3.5 text-success" />
          Gần nhất: {page.lastPublished.articleTitle}
        </p>
      )}
      {page.warnings.map((w) => (
        <p key={w} className="mt-2 flex gap-1.5 text-xs text-warning">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" /> {w}
        </p>
      ))}

      {onOpen && (
        <div className="mt-auto pt-3">
          <Button size="sm" className="w-full" onClick={onOpen}>
            Quản lý Page
          </Button>
        </div>
      )}
    </Card>
  );
}

/** Details of one run: story, progress, message, links. */
export function RunDetails({
  run,
  onCancel,
  showPage,
}: {
  run: AutoPostRunDto;
  onCancel?: (run: AutoPostRunDto) => void;
  showPage?: boolean;
}) {
  return (
    <div className="min-w-0 flex-1 space-y-1.5">
      <div className="flex flex-wrap items-center gap-2">
        <StateBadge state={run.status} />
        {run.isBreaking && <Badge tone="danger">Tin nóng</Badge>}
        {showPage && run.pageName && <Badge tone="neutral">{run.pageName}</Badge>}
        {run.region && <RegionBadge region={run.region} />}
        {run.hotScore != null && <span className="text-xs text-fg-subtle">Độ hot {Math.round(run.hotScore)}</span>}
        {run.attempts > 1 && <span className="text-xs text-fg-subtle">Lần thử {run.attempts}</span>}
        {run.isManual && <span className="text-xs text-fg-subtle">Đăng ngay</span>}
      </div>
      {run.articleTitle && (
        <p className="text-sm font-medium leading-snug text-fg">
          {run.articleUrl ? (
            <a href={run.articleUrl} target="_blank" rel="noreferrer" className="hover:underline">
              {run.articleTitle}
            </a>
          ) : (
            run.articleTitle
          )}
          {run.sourceName && <span className="ml-1.5 font-normal text-fg-muted">· {run.sourceName}</span>}
        </p>
      )}
      {run.status === 'Producing' && run.progressPercentage != null && (
        <ProgressBar value={run.progressPercentage} className="max-w-sm" />
      )}
      {run.message && (
        <p className={cn('text-[13px]', run.status === 'Failed' ? 'text-danger' : 'text-fg-muted')}>{run.message}</p>
      )}
      {run.publishedAtUtc && run.status === 'Published' && (
        <p className="flex items-center gap-1 text-xs text-fg-subtle">
          <CheckCircle2 className="h-3.5 w-3.5 text-success" /> Đăng lúc {formatDateTime(run.publishedAtUtc)}
        </p>
      )}
      <div className="flex flex-wrap gap-2 pt-0.5">
        {run.videoProjectId && (
          <ButtonLink size="sm" variant="ghost" href={href(`/videos/${run.videoProjectId}`)} icon={<Film className="h-3.5 w-3.5" />}>
            Xem video
          </ButtonLink>
        )}
        {run.facebookPostUrl && (
          <ButtonLink
            size="sm"
            variant="ghost"
            href={run.facebookPostUrl}
            target="_blank"
            rel="noreferrer"
            icon={<ExternalLink className="h-3.5 w-3.5" />}
          >
            Xem trên Facebook
          </ButtonLink>
        )}
        {onCancel && (run.status === 'Producing' || run.status === 'WaitingSlot') && (
          <Button size="sm" variant="danger-soft" icon={<X className="h-3.5 w-3.5" />} onClick={() => onCancel(run)}>
            Huỷ lượt
          </Button>
        )}
      </div>
    </div>
  );
}
