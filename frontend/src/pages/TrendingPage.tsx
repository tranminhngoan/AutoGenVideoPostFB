import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Flame, RefreshCw, Search, ExternalLink, Globe2, Newspaper, TrendingUp, SlidersHorizontal, Sparkles } from 'lucide-react';
import { api, TrendingArticleDto, TrendingOverviewDto } from '../services/api';
import { Badge, Button, ButtonLink, Card, EmptyState, IconButton, Input, PageHeader, Segmented, Skeleton, Tabs, Thumb, cn } from '../components/ui';
import { useToast } from '../components/feedback';
import { createVideoFromUrl } from '../lib/actions';
import { href, navigate } from '../lib/router';
import { errorText, timeAgo } from '../lib/format';

type Region = 'all' | 'VN' | 'World';

const WINDOWS = [
  { value: 6, label: '6 giờ' },
  { value: 24, label: '24 giờ' },
  { value: 72, label: '3 ngày' },
];

interface TrendingPageProps {
  onCreated: () => void;
}

export const TrendingPage: React.FC<TrendingPageProps> = ({ onCreated }) => {
  const toast = useToast();
  const [region, setRegion] = useState<Region>('all');
  const [hours, setHours] = useState(24);
  const [source, setSource] = useState<string>('');
  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [data, setData] = useState<TrendingOverviewDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [creatingId, setCreatingId] = useState<string | null>(null);

  useEffect(() => {
    const t = setTimeout(() => setDebounced(search.trim()), 300);
    return () => clearTimeout(t);
  }, [search]);

  const load = useCallback(
    async (silent = false) => {
      if (!silent) setLoading(true);
      try {
        setData(await api.getTrending({ region, hours, source: source || undefined, q: debounced || undefined, limit: 80 }));
      } catch (err) {
        if (!silent) toast.error('Không tải được tin hot', errorText(err, ''));
      } finally {
        if (!silent) setLoading(false);
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [region, hours, source, debounced]
  );

  useEffect(() => {
    load();
  }, [load]);

  // The Worker refreshes feeds every 30 minutes — pick up new rankings quietly
  useEffect(() => {
    const t = setInterval(() => load(true), 5 * 60 * 1000);
    return () => clearInterval(t);
  }, [load]);

  const refreshNow = async () => {
    setRefreshing(true);
    try {
      const r = await api.refreshTrending();
      toast.success(
        `Đã cập nhật ${r.feedsOk} nguồn`,
        `${r.newItems} tin mới${r.feedsFailed ? `, ${r.feedsFailed} nguồn lỗi` : ''}.`
      );
      await load(true);
    } catch (err) {
      toast.error('Cập nhật thất bại', errorText(err, ''));
    } finally {
      setRefreshing(false);
    }
  };

  const quickCreate = async (item: TrendingArticleDto) => {
    setCreatingId(item.id);
    try {
      const project = await createVideoFromUrl({ url: item.url });
      toast.success('Đã bắt đầu tạo video', item.language !== 'vi' ? 'Bài tiếng nước ngoài — AI sẽ viết kịch bản bằng tiếng Việt.' : undefined);
      onCreated();
      navigate(`/videos/${project.id}`);
    } catch (err) {
      toast.error('Không tạo được video', errorText(err, ''));
    } finally {
      setCreatingId(null);
    }
  };

  const visibleSources = useMemo(
    () => (data?.sources ?? []).filter((s) => region === 'all' || s.region === region),
    [data, region]
  );

  const last = data?.lastRefresh;

  return (
    <div>
      <PageHeader
        title="Tin đang hot"
        description={
          <>
            Tự cập nhật mỗi 30 phút từ báo trong nước và quốc tế.{' '}
            {last ? (
              <span className="text-fg-subtle">
                Lần cuối {timeAgo(last.finishedAtUtc).toLowerCase()} · {last.feedsOk} nguồn
                {last.feedsFailed > 0 && `, ${last.feedsFailed} nguồn lỗi`}
              </span>
            ) : (
              <span className="text-fg-subtle">Chưa cập nhật lần nào.</span>
            )}
          </>
        }
        actions={
          <Button icon={<RefreshCw className={cn('h-4 w-4', refreshing && 'animate-spin')} />} onClick={refreshNow} disabled={refreshing}>
            <span className="hidden sm:inline">{refreshing ? 'Đang cập nhật…' : 'Cập nhật ngay'}</span>
            <span className="sm:hidden">{refreshing ? 'Đang…' : 'Cập nhật'}</span>
          </Button>
        }
      />

      {/* Filters */}
      <div className="mb-3 flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
        <Tabs
          className="md:flex-1"
          value={region}
          onChange={(r) => {
            setRegion(r);
            setSource('');
          }}
          items={[
            { id: 'all', label: 'Tất cả' },
            { id: 'VN', label: 'Việt Nam' },
            { id: 'World', label: 'Quốc tế' },
          ]}
        />
        <div className="flex items-center gap-2 md:pb-1.5">
          <Segmented size="sm" value={hours} onChange={setHours} options={WINDOWS} />
          <div className="w-full md:w-56">
            <Input leading={<Search />} value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Tìm trong tin hot…" />
          </div>
        </div>
      </div>

      {visibleSources.length > 0 && (
        <div className="scrollbar-none -mx-1 mb-3 flex gap-1.5 overflow-x-auto px-1 pb-1">
          <SourceChip active={!source} onClick={() => setSource('')} label="Tất cả nguồn" />
          {visibleSources.map((s) => (
            <SourceChip
              key={s.key}
              active={source === s.key}
              onClick={() => setSource(source === s.key ? '' : s.key)}
              label={s.name}
              count={s.itemCount}
            />
          ))}
        </div>
      )}

      {data && data.trendKeywords.length > 0 && (
        <div className="scrollbar-none -mx-1 mb-5 flex items-center gap-1.5 overflow-x-auto px-1 text-xs sm:flex-wrap">
          <span className="flex shrink-0 items-center gap-1 text-fg-muted">
            <TrendingUp className="h-3.5 w-3.5" /> Google đang tìm:
          </span>
          {data.trendKeywords.slice(0, 12).map((k) => (
            <button
              key={k}
              type="button"
              onClick={() => setSearch(k)}
              className="shrink-0 rounded-full border border-line bg-surface px-2 py-0.5 text-fg-muted transition-colors hover:border-line-strong hover:text-fg"
            >
              {k}
            </button>
          ))}
        </div>
      )}

      <Card className="overflow-hidden">
        {loading && !data ? (
          <div className="divide-y divide-line">
            {Array.from({ length: 6 }).map((_, i) => (
              <div key={i} className="flex gap-4 p-4">
                <Skeleton className="h-6 w-6" />
                <Skeleton className="aspect-video w-32" />
                <div className="flex-1 space-y-2">
                  <Skeleton className="h-3 w-1/4" />
                  <Skeleton className="h-4 w-4/5" />
                  <Skeleton className="h-3 w-full" />
                </div>
              </div>
            ))}
          </div>
        ) : !data || data.items.length === 0 ? (
          <EmptyState
            icon={<Flame />}
            title={data?.lastRefresh ? 'Không có tin phù hợp' : 'Chưa có dữ liệu tin hot'}
            description={
              data?.lastRefresh
                ? 'Thử đổi khu vực, khoảng thời gian hoặc từ khoá.'
                : 'Worker sẽ tự lấy tin khi khởi động. Bạn cũng có thể bấm “Cập nhật ngay”.'
            }
            action={
              !data?.lastRefresh && (
                <Button variant="primary" loading={refreshing} onClick={refreshNow}>
                  Cập nhật ngay
                </Button>
              )
            }
          />
        ) : (
          <ol className="divide-y divide-line">
            {data.items.map((item, idx) => (
              <TrendingRow
                key={item.id}
                rank={idx + 1}
                item={item}
                creating={creatingId === item.id}
                disabled={creatingId !== null}
                onCreate={() => quickCreate(item)}
              />
            ))}
          </ol>
        )}
      </Card>

      <p className="mt-3 px-1 text-xs text-fg-subtle">
        Độ hot dựa trên: số báo cùng đưa một sự kiện, từ khoá đang được tìm nhiều trên Google, tin nổi bật của toà soạn và độ
        mới (giảm một nửa sau mỗi 10 giờ).
      </p>
    </div>
  );
};

function SourceChip({ active, onClick, label, count }: { active: boolean; onClick: () => void; label: string; count?: number }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex h-7 shrink-0 items-center gap-1.5 rounded-full border px-3 text-xs transition-colors',
        active ? 'border-fg bg-fg text-canvas' : 'border-line bg-surface text-fg-muted hover:border-line-strong hover:text-fg'
      )}
    >
      {label}
      {count !== undefined && <span className={cn('tabular', active ? 'text-canvas/70' : 'text-fg-subtle')}>{count}</span>}
    </button>
  );
}

function HotScore({ score }: { score: number }) {
  const tone = score >= 75 ? 'text-danger' : score >= 50 ? 'text-warning' : 'text-fg-subtle';
  return (
    <div className={cn('flex items-center gap-1 text-sm font-semibold tabular', tone)} title="Điểm độ hot (0–100)">
      <Flame className="h-4 w-4" />
      {Math.round(score)}
    </div>
  );
}

function TrendingRow({
  rank,
  item,
  creating,
  disabled,
  onCreate,
}: {
  rank: number;
  item: TrendingArticleDto;
  creating: boolean;
  disabled: boolean;
  onCreate: () => void;
}) {
  const foreign = item.region === 'World';
  return (
    <li className="flex gap-3 p-4 transition-colors hover:bg-subtle sm:gap-4">
      <div className="w-6 shrink-0 pt-0.5 text-right text-sm font-semibold tabular text-fg-subtle">{rank}</div>

      <a href={item.url} target="_blank" rel="noreferrer" className="hidden shrink-0 sm:block">
        <Thumb src={item.imageUrl} className="aspect-video w-36 rounded-md border border-line" fallback={<Newspaper className="h-4 w-4" />} />
      </a>

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-1.5 gap-y-1 text-xs text-fg-muted">
          <span className="font-medium text-fg">{item.sourceName}</span>
          <span className="text-fg-subtle">·</span>
          <span>{timeAgo(item.publishedAtUtc)}</span>
          {foreign && (
            <Badge className="ml-1">
              <Globe2 className="h-3 w-3" /> Quốc tế
            </Badge>
          )}
        </div>

        <a
          href={item.url}
          target="_blank"
          rel="noreferrer"
          className="mt-1 block text-[15px] font-medium leading-snug text-fg hover:underline"
        >
          {item.title}
        </a>
        {item.summary && <p className="mt-1 line-clamp-2 text-[13px] text-fg-muted">{item.summary}</p>}

        <div className="mt-2 flex flex-wrap items-center gap-1.5">
          {item.coverageCount > 1 && (
            <Badge tone="info" className="cursor-default">
              <span title={item.relatedSources.length ? `Cũng có trên: ${item.relatedSources.join(', ')}` : undefined}>
                {item.coverageCount} báo cùng đưa tin
              </span>
            </Badge>
          )}
          {item.trendKeyword && (
            <Badge tone="warning">
              <TrendingUp className="h-3 w-3" /> Google: {item.trendKeyword}
            </Badge>
          )}
          {item.isFeatured && <Badge>Nổi bật</Badge>}
          {item.relatedSources.length > 0 && (
            <span className="truncate text-xs text-fg-subtle">Cũng có trên {item.relatedSources.slice(0, 4).join(', ')}{item.relatedSources.length > 4 ? '…' : ''}</span>
          )}
        </div>

        {/* Mobile: score + actions under the text so the title keeps the full width */}
        <div className="mt-3 flex items-center justify-between sm:hidden">
          <HotScore score={item.hotScore} />
          <RowActions item={item} foreign={foreign} creating={creating} disabled={disabled} onCreate={onCreate} />
        </div>
      </div>

      <div className="hidden shrink-0 flex-col items-end justify-between gap-2 sm:flex">
        <HotScore score={item.hotScore} />
        <RowActions item={item} foreign={foreign} creating={creating} disabled={disabled} onCreate={onCreate} />
      </div>
    </li>
  );
}

function RowActions({
  item,
  foreign,
  creating,
  disabled,
  onCreate,
}: {
  item: TrendingArticleDto;
  foreign: boolean;
  creating: boolean;
  disabled: boolean;
  onCreate: () => void;
}) {
  return (
        <div className="flex items-center gap-1">
          <IconButton label="Mở bài gốc" size="sm" onClick={() => window.open(item.url, '_blank', 'noreferrer')}>
            <ExternalLink className="h-3.5 w-3.5" />
          </IconButton>
          <ButtonLink
            size="sm"
            variant="ghost"
            href={href(`/create?url=${encodeURIComponent(item.url)}`)}
            title="Mở trình tạo để chọn giọng, thời lượng…"
            className="hidden px-2 sm:inline-flex"
          >
            <SlidersHorizontal className="h-3.5 w-3.5" />
          </ButtonLink>
          <Button
            size="sm"
            variant="primary"
            icon={<Sparkles className="h-3.5 w-3.5" />}
            loading={creating}
            disabled={disabled && !creating}
            onClick={onCreate}
            title={foreign ? 'Bài tiếng nước ngoài — AI sẽ viết kịch bản tiếng Việt' : undefined}
          >
            Tạo video
          </Button>
        </div>
  );
}
