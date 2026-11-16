import React, { useEffect, useState } from 'react';
import { Lightbulb } from 'lucide-react';
import { api, InsightBucketDto, PageInsightsDto } from '../services/api';
import { Badge, Button, Card, CardBody, CardHeader, ProgressBar, cn } from './ui';

const fmt = (n: number) => (n >= 1000 ? `${(n / 1000).toFixed(n >= 10000 ? 0 : 1)}k` : String(Math.round(n)));

/** Average views of videos posted in each hour of the day (Vietnam time). Single series, accent colour. */
function HoursChart({ hours }: { hours: InsightBucketDto[] }) {
  const [hover, setHover] = useState<number | null>(null);
  const byHour = new Map(hours.map((h) => [Number(h.key), h]));
  const range = Array.from({ length: 18 }, (_, i) => i + 6); // 06:00 – 23:00
  const max = Math.max(1, ...hours.map((h) => h.averageViews));
  return (
    <div>
      <div className="relative flex h-32 items-end gap-[2px] border-b border-line">
        <div className="pointer-events-none absolute inset-x-0 border-t border-dashed border-line" style={{ bottom: '100%' }}>
          <span className="absolute -top-4 left-0 text-[11px] tabular-nums text-fg-subtle">{fmt(max)} lượt xem TB</span>
        </div>
        {range.map((hr, i) => {
          const b = byHour.get(hr);
          const v = b?.averageViews ?? 0;
          return (
            <div key={hr} className="relative flex h-full flex-1 items-end" onMouseEnter={() => setHover(hr)} onMouseLeave={() => setHover(null)}>
              <div
                className={cn('w-full rounded-t-[4px] bg-accent transition-opacity', hover != null && hover !== hr && 'opacity-50')}
                style={{ height: v > 0 ? `max(2px, ${(v / max) * 100}%)` : 0 }}
              />
              {hover === hr && (
                <div
                  className={cn(
                    'pointer-events-none absolute z-10 mb-1.5 whitespace-nowrap rounded-md border border-line bg-surface px-2 py-1 text-xs shadow-card',
                    i < 3 ? 'left-0' : i > range.length - 4 ? 'right-0' : 'left-1/2 -translate-x-1/2'
                  )}
                  style={{ bottom: `min(calc(${(v / max) * 100}% + 4px), calc(100% - 28px))` }}
                >
                  <span className="text-fg-muted">{String(hr).padStart(2, '0')}:00</span>{' '}
                  {b ? (
                    <>
                      <b className="tabular-nums text-fg">{fmt(v)}</b> <span className="text-fg-muted">lượt xem TB · {b.videos} video</span>
                    </>
                  ) : (
                    <span className="text-fg-muted">chưa đăng giờ này</span>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>
      <div className="mt-1 flex h-4 gap-[2px]">
        {range.map((hr) => (
          <span key={hr} className="relative flex-1">
            {hr % 3 === 0 && <span className="absolute left-1/2 -translate-x-1/2 text-[11px] text-fg-subtle">{hr}h</span>}
          </span>
        ))}
      </div>
    </div>
  );
}

function BucketList({ buckets, minVideos }: { buckets: InsightBucketDto[]; minVideos: number }) {
  const max = Math.max(1, ...buckets.map((b) => b.averageViews));
  return (
    <ul className="space-y-2">
      {buckets.slice(0, 6).map((b) => (
        <li key={b.key} className="text-[13px]">
          <div className="mb-1 flex items-center justify-between gap-2">
            <span className="truncate text-fg">{b.label}</span>
            <span className="shrink-0 tabular-nums text-fg-muted">
              {fmt(b.averageViews)} TB · {b.videos} video
              {b.videos >= minVideos && b.ratio >= 1.15 && <Badge tone="success" className="ml-1.5">Hút view</Badge>}
            </span>
          </div>
          <ProgressBar value={(b.averageViews / max) * 100} />
        </li>
      ))}
    </ul>
  );
}

/** "Tự học": what this page's audience watches most, and the posting times that follow from it. */
export function PageInsightsCard({
  pageId,
  autoTune,
  currentSlots,
  onApplySlots,
}: {
  pageId: string;
  autoTune: boolean;
  currentSlots: string[];
  onApplySlots: (slots: string[]) => Promise<void> | void;
}) {
  const [data, setData] = useState<PageInsightsDto | null>(null);
  const [applying, setApplying] = useState(false);

  useEffect(() => {
    api.getPageInsights(pageId).then(setData).catch(() => setData(null));
  }, [pageId]);

  if (!data) return null;
  const sameSlots = data.suggestedSlots.join(',') === [...currentSlots].sort().join(',');

  return (
    <Card>
      <CardHeader
        title="Học từ lượt xem"
        description={
          data.enoughData
            ? `Từ ${data.samples} video đã đăng trên 1 ngày. Trung bình ${fmt(data.averageViews)} lượt xem/video.` +
              (autoTune ? ' Đang tự ưu tiên loại tin hút view khi chọn tin.' : '')
            : `Cần ít nhất ${data.minSamples} video có số liệu (đang có ${data.samples}) để rút ra giờ vàng và loại tin hút view.`
        }
      />
      <CardBody className="space-y-5 pt-3">
        {!data.enoughData && <ProgressBar value={(data.samples / data.minSamples) * 100} />}
        {data.hours.length > 0 && (
          <div>
            <p className="mb-5 text-xs font-medium uppercase tracking-wide text-fg-subtle">Lượt xem theo giờ đăng</p>
            <HoursChart hours={data.hours} />
          </div>
        )}
        {(data.topics.length > 0 || data.regions.length > 0) && (
          <div className="grid gap-5 sm:grid-cols-2">
            {data.topics.length > 0 && (
              <div>
                <p className="mb-2 text-xs font-medium uppercase tracking-wide text-fg-subtle">Loại tin</p>
                <BucketList buckets={data.topics} minVideos={3} />
              </div>
            )}
            {data.regions.length > 0 && (
              <div>
                <p className="mb-2 text-xs font-medium uppercase tracking-wide text-fg-subtle">Khu vực</p>
                <BucketList buckets={data.regions} minVideos={3} />
              </div>
            )}
          </div>
        )}
        {data.suggestedSlots.length > 0 && (
          <div className="flex flex-wrap items-center gap-3 rounded-md border border-line bg-subtle px-3 py-2.5 text-[13px]">
            <Lightbulb className="h-4 w-4 shrink-0 text-accent" />
            <span className="min-w-0 flex-1 text-fg">
              Giờ vàng gợi ý: <b className="font-mono">{data.suggestedSlots.join(', ')}</b>
            </span>
            <Button
              size="sm"
              disabled={sameSlots}
              loading={applying}
              onClick={async () => {
                setApplying(true);
                try {
                  await onApplySlots(data.suggestedSlots);
                } finally {
                  setApplying(false);
                }
              }}
            >
              {sameSlots ? 'Đang dùng' : 'Áp dụng'}
            </Button>
          </div>
        )}
      </CardBody>
    </Card>
  );
}
