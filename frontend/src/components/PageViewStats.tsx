import React, { useEffect, useState } from 'react';
import { AlertTriangle, ArrowDownRight, ArrowUpRight, ExternalLink, RefreshCw } from 'lucide-react';
import { api, PageStatsOverviewDto, PageViewStatsDto } from '../services/api';
import { Button, Card, CardHeader, Skeleton, cn } from './ui';
import { PageAvatar } from './AutoPostWidgets';
import { useToast } from './feedback';
import { formatNumber, timeAgo, errorText } from '../lib/format';
import { href } from '../lib/router';

const compact = (n: number) => n.toLocaleString('vi-VN', { notation: 'compact', maximumFractionDigits: 1 });

/** "2026-11-16" → "16/11" */
const dayLabel = (iso: string) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}`;

function Change({ now, before }: { now: number; before: number }) {
  if (before <= 0) return <span className="text-fg-subtle">7 ngày trước: chưa có số liệu</span>;
  const pct = Math.round(((now - before) / before) * 100);
  const Icon = pct >= 0 ? ArrowUpRight : ArrowDownRight;
  return (
    <span className="inline-flex items-center gap-0.5 text-fg-muted">
      <Icon className="h-3.5 w-3.5" />
      {pct >= 0 ? '+' : ''}
      {pct}% so với 7 ngày trước
    </span>
  );
}

/** Views gained per day: one series, so one colour and no legend; hover a column for its numbers. */
function DailyViewsChart({ dates, values }: { dates: string[]; values: number[] }) {
  const [hover, setHover] = useState<number | null>(null);
  const max = Math.max(1, ...values);
  const labelEvery = dates.length > 14 ? 7 : dates.length > 7 ? 3 : 1;
  return (
    <div>
      <div className="relative flex h-40 items-end gap-[2px] border-b border-line pl-10">
        {/* recessive gridlines + y labels */}
        {[1, 0.5].map((f) => (
          <div key={f} className="pointer-events-none absolute left-10 right-0 border-t border-dashed border-line" style={{ bottom: `${f * 100}%` }}>
            <span className="absolute -left-10 -top-2 w-9 text-right text-[11px] tabular-nums text-fg-subtle">{compact(max * f)}</span>
          </div>
        ))}
        <span className="absolute -bottom-2 left-0 w-9 text-right text-[11px] text-fg-subtle">0</span>
        {values.map((v, i) => (
          <div
            key={dates[i]}
            className="relative flex h-full flex-1 items-end"
            onMouseEnter={() => setHover(i)}
            onMouseLeave={() => setHover(null)}
          >
            <div
              className={cn('w-full rounded-t-[4px] bg-accent transition-opacity', hover != null && hover !== i && 'opacity-50')}
              style={{ height: v > 0 ? `max(2px, ${(v / max) * 100}%)` : 0 }}
            />
            {hover === i && (
              <div
                className={cn(
                  'pointer-events-none absolute z-10 mb-1.5 whitespace-nowrap rounded-md border border-line bg-surface px-2 py-1 text-xs shadow-card',
                  // keep the tooltip inside the chart at both ends
                  i < 2 ? 'left-0' : i > values.length - 3 ? 'right-0' : 'left-1/2 -translate-x-1/2'
                )}
                style={{ bottom: `min(calc(${(v / max) * 100}% + 4px), calc(100% - 28px))` }}
              >
                <span className="text-fg-muted">{dayLabel(dates[i])}</span>{' '}
                <b className="tabular-nums text-fg">{formatNumber(v)}</b> <span className="text-fg-muted">lượt xem</span>
              </div>
            )}
          </div>
        ))}
      </div>
      <div className="mt-1 flex h-4 gap-[2px] pl-10">
        {dates.map((d, i) => (
          <span key={d} className="relative flex-1">
            {(dates.length - 1 - i) % labelEvery === 0 && (
              <span className="absolute left-1/2 -translate-x-1/2 whitespace-nowrap text-[11px] text-fg-subtle">{dayLabel(d)}</span>
            )}
          </span>
        ))}
      </div>
    </div>
  );
}

function Sparkline({ dates, values }: { dates: string[]; values: number[] }) {
  const max = Math.max(1, ...values);
  return (
    <div className="flex h-7 w-28 items-end gap-px" aria-hidden>
      {values.map((v, i) => (
        <div key={dates[i]} className="flex h-full flex-1 items-end" title={`${dayLabel(dates[i])}: ${formatNumber(v)} lượt xem`}>
          <div className="w-full rounded-t-[2px] bg-accent" style={{ height: v > 0 ? `max(2px, ${(v / max) * 100}%)` : 0 }} />
        </div>
      ))}
    </div>
  );
}

function SyncWarning({ error }: { error?: string | null }) {
  if (!error) return null;
  return (
    <p className="mt-1.5 flex max-w-[320px] gap-1 text-xs text-warning" title={error}>
      <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
      <span className="line-clamp-2">
        {error.includes('read_insights') ? 'Token chưa có quyền read_insights nên chưa đọc được lượt xem.' : error}
      </span>
    </p>
  );
}

/** Phones: one compact card per page instead of the wide table. */
function PageCardMobile({ page, dates }: { page: PageViewStatsDto; dates: string[] }) {
  const figures = [
    { label: 'Tổng', value: page.totalViews },
    { label: '7 ngày', value: page.views7Days },
    { label: 'Hôm nay', value: page.viewsToday },
  ];
  return (
    <li className="border-t border-line px-5 py-3">
      <div className="flex items-center gap-2.5">
        <PageAvatar name={page.pageName} enabled={page.isActive} className="h-8 w-8" />
        <div className="min-w-0 flex-1">
          <p className="truncate text-[13px] font-medium text-fg">{page.pageName}</p>
          <p className="text-xs text-fg-subtle">
            {page.topicName ?? 'Chưa đặt chủ đề'} · {page.videos} video
          </p>
        </div>
        <Sparkline dates={dates} values={page.dailyViews} />
      </div>
      <SyncWarning error={page.syncError} />
      <dl className="mt-2 grid grid-cols-3 gap-2">
        {figures.map((f) => (
          <div key={f.label}>
            <dt className="text-[11px] text-fg-subtle">{f.label}</dt>
            <dd className="text-[15px] font-semibold tabular-nums text-fg">{formatNumber(f.value)}</dd>
          </div>
        ))}
      </dl>
      {page.topVideo && (
        <p className="mt-1.5 truncate text-xs text-fg-muted">
          Nhiều lượt xem nhất:{' '}
          <a href={href(`/videos/${page.topVideo.videoProjectId}`)} className="text-fg hover:underline">
            {page.topVideo.title}
          </a>{' '}
          ({formatNumber(page.topVideo.views)})
        </p>
      )}
    </li>
  );
}

function PageRow({ page, dates }: { page: PageViewStatsDto; dates: string[] }) {
  return (
    <tr className="border-t border-line align-top">
      <td className="py-3 pl-5 pr-3">
        <div className="flex items-center gap-2.5">
          <PageAvatar name={page.pageName} enabled={page.isActive} className="h-8 w-8" />
          <div className="min-w-0">
            <p className="max-w-[200px] truncate font-medium text-fg" title={page.pageName}>
              {page.pageName}
            </p>
            <p className="text-xs text-fg-subtle">
              {page.topicName ?? 'Chưa đặt chủ đề'} · {page.videos} video
            </p>
          </div>
        </div>
        <SyncWarning error={page.syncError} />
      </td>
      <td className="px-3 py-3 text-right font-semibold tabular-nums text-fg">{formatNumber(page.totalViews)}</td>
      <td className="px-3 py-3 text-right tabular-nums text-fg">{formatNumber(page.views7Days)}</td>
      <td className="px-3 py-3 text-right tabular-nums text-fg">{formatNumber(page.viewsToday)}</td>
      <td className="px-3 py-3 text-right tabular-nums text-fg-muted">{formatNumber(page.averageViewsPerVideo)}</td>
      <td className="px-3 py-3">
        <Sparkline dates={dates} values={page.dailyViews} />
      </td>
      <td className="py-3 pl-3 pr-5">
        {page.topVideo ? (
          <div className="max-w-[240px]">
            <a href={href(`/videos/${page.topVideo.videoProjectId}`)} className="line-clamp-2 text-fg hover:underline">
              {page.topVideo.title}
            </a>
            <p className="mt-0.5 flex items-center gap-1.5 text-xs text-fg-muted">
              <span className="tabular-nums">{formatNumber(page.topVideo.views)} lượt xem</span>
              {page.topVideo.postUrl && (
                <a href={page.topVideo.postUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-0.5 hover:text-fg">
                  <ExternalLink className="h-3 w-3" /> Facebook
                </a>
              )}
            </p>
          </div>
        ) : (
          <span className="text-xs text-fg-subtle">—</span>
        )}
      </td>
    </tr>
  );
}

/** Dashboard section: Reels views of every Facebook Page, read from Facebook every 30 minutes. */
export function PageViewStats() {
  const toast = useToast();
  const [stats, setStats] = useState<PageStatsOverviewDto | null>(null);
  const [failed, setFailed] = useState(false);
  const [syncing, setSyncing] = useState(false);

  const load = () =>
    api
      .getPageStats(14)
      .then((s) => {
        setStats(s);
        setFailed(false);
      })
      .catch(() => setFailed(true));

  useEffect(() => {
    load();
    const t = setInterval(load, 5 * 60 * 1000);
    return () => clearInterval(t);
  }, []);

  const syncNow = async () => {
    setSyncing(true);
    try {
      const r = await api.syncPageStats();
      await load();
      if (r.errors.length > 0 && r.postsUpdated === 0) toast.error('Chưa đọc được số liệu', r.errors[0]);
      else if (r.postsChecked === 0) toast.success('Số liệu đã mới nhất', 'Các video vừa được cập nhật trong 15 phút qua.');
      else toast.success('Đã cập nhật lượt xem', `${r.postsUpdated}/${r.postsChecked} video lấy được số liệu mới.`);
    } catch (err) {
      toast.error('Không cập nhật được', errorText(err, 'Không kết nối được máy chủ.'));
    } finally {
      setSyncing(false);
    }
  };

  if (failed && !stats) return null;

  const t = stats?.totals;
  const withStats = stats?.pages.reduce((n, p) => n + p.videosWithStats, 0) ?? 0;
  const tiles = t
    ? [
        { label: 'Tổng lượt xem', value: formatNumber(t.totalViews), hint: `${formatNumber(t.videos)} video đã đăng` },
        { label: 'Hôm nay', value: formatNumber(t.viewsToday) },
        { label: '7 ngày qua', value: formatNumber(t.views7Days), hint: <Change now={t.views7Days} before={t.viewsPrevious7Days} /> },
        { label: 'TB mỗi video', value: formatNumber(withStats ? Math.round(t.totalViews / withStats) : 0) },
      ]
    : [];

  return (
    <Card className="mb-6">
      <CardHeader
        title="Lượt xem theo Page"
        description={
          <>
            Lượt xem Reels (tính cả xem lại) lấy từ Facebook, tự cập nhật mỗi 30 phút
            {stats?.lastSyncUtc ? ` · lần gần nhất ${timeAgo(stats.lastSyncUtc).toLowerCase()}` : ''}.
          </>
        }
        actions={
          <Button size="sm" icon={<RefreshCw className={cn('h-3.5 w-3.5', syncing && 'animate-spin')} />} onClick={syncNow} disabled={syncing}>
            Cập nhật ngay
          </Button>
        }
      />

      {!stats ? (
        <div className="space-y-3 p-5">
          <Skeleton className="h-16 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : stats.pages.length === 0 || stats.totals.videos === 0 ? (
        <p className="px-5 pb-5 pt-3 text-[13px] text-fg-muted">
          Chưa có video nào được đăng lên Facebook. Lượt xem của từng Page sẽ hiện ở đây sau khi video đầu tiên được đăng.
        </p>
      ) : (
        <>
          <div className="mx-5 mt-4 grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-line bg-line lg:grid-cols-4">
            {tiles.map((tile) => (
              <div key={tile.label} className="bg-surface px-4 py-3">
                <p className="text-xs text-fg-muted">{tile.label}</p>
                <p className="mt-1 text-2xl font-semibold tabular-nums text-fg">{tile.value}</p>
                {tile.hint && <p className="mt-0.5 text-[11px] text-fg-subtle">{tile.hint}</p>}
              </div>
            ))}
          </div>

          <div className="px-5 pt-5">
            <p className="mb-3 text-[13px] font-medium text-fg">Lượt xem mỗi ngày · tất cả Page · {stats.days} ngày</p>
            <DailyViewsChart dates={stats.dates} values={stats.dailyViews} />
          </div>

          <ul className="mt-4 md:hidden">
            {stats.pages.map((p) => (
              <PageCardMobile key={p.facebookAccountId} page={p} dates={stats.dates} />
            ))}
          </ul>

          <div className="mt-4 hidden overflow-x-auto md:block">
            <table className="w-full min-w-[860px] text-[13px]">
              <thead>
                <tr className="whitespace-nowrap text-xs text-fg-muted">
                  <th className="py-2 pl-5 pr-3 text-left font-medium">Page</th>
                  <th className="px-3 py-2 text-right font-medium">Tổng lượt xem</th>
                  <th className="px-3 py-2 text-right font-medium">7 ngày</th>
                  <th className="px-3 py-2 text-right font-medium">Hôm nay</th>
                  <th className="px-3 py-2 text-right font-medium">TB / video</th>
                  <th className="px-3 py-2 text-left font-medium">{stats.days} ngày</th>
                  <th className="py-2 pl-3 pr-5 text-left font-medium">Video nhiều lượt xem nhất</th>
                </tr>
              </thead>
              <tbody>
                {stats.pages.map((p) => (
                  <PageRow key={p.facebookAccountId} page={p} dates={stats.dates} />
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </Card>
  );
}
