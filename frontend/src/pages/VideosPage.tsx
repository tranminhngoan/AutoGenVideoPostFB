import React, { useEffect, useMemo, useState } from 'react';
import { Plus, RefreshCw, Search, Film, ChevronRight } from 'lucide-react';
import { VideoProject } from '../types';
import { Button, ButtonLink, Card, EmptyState, IconButton, Input, PageHeader, Skeleton, Tabs } from '../components/ui';
import { StatusBadge, ReelThumb, projectThumb } from '../components/VideoBits';
import { href, navigate, useRoute } from '../lib/router';
import { domainOf, formatClock, statusMeta, timeAgo } from '../lib/format';

const PAGE = 25;

type Filter = 'all' | 'processing' | 'review' | 'published' | 'failed';

interface VideosPageProps {
  projects: VideoProject[] | null;
  onRefresh: () => Promise<unknown>;
}

const FILTERS: { id: Filter; label: string }[] = [
  { id: 'all', label: 'Tất cả' },
  { id: 'processing', label: 'Đang xử lý' },
  { id: 'review', label: 'Chờ duyệt' },
  { id: 'published', label: 'Đã đăng' },
  { id: 'failed', label: 'Lỗi' },
];

function matches(p: VideoProject, f: Filter) {
  if (f === 'all') return true;
  const g = statusMeta(p.status).group;
  if (f === 'processing') return g === 'processing' || g === 'queued';
  return g === f;
}

export const VideosPage: React.FC<VideosPageProps> = ({ projects, onRefresh }) => {
  const route = useRoute();
  const initial = (route.query.get('filter') as Filter) || 'all';
  const [filter, setFilter] = useState<Filter>(FILTERS.some((f) => f.id === initial) ? initial : 'all');
  const [query, setQuery] = useState('');
  const [refreshing, setRefreshing] = useState(false);
  const [limit, setLimit] = useState(PAGE);

  useEffect(() => setLimit(PAGE), [filter, query]);

  const list = projects ?? [];
  const counts = useMemo(() => {
    const c: Record<Filter, number> = { all: 0, processing: 0, review: 0, published: 0, failed: 0 };
    list.forEach((p) => FILTERS.forEach((f) => matches(p, f.id) && c[f.id]++));
    return c;
  }, [list]);

  const q = query.trim().toLowerCase();
  const visible = list.filter(
    (p) =>
      matches(p, filter) &&
      (!q || p.title.toLowerCase().includes(q) || (p.publisher || '').toLowerCase().includes(q) || p.sourceUrl.toLowerCase().includes(q))
  );

  const refresh = async () => {
    setRefreshing(true);
    await onRefresh();
    setRefreshing(false);
  };

  return (
    <div>
      <PageHeader
        title="Video"
        description="Tất cả video đã tạo, mới nhất ở trên."
        actions={
          <>
            <IconButton label="Làm mới" onClick={refresh} className="border border-line-strong bg-surface shadow-card">
              <RefreshCw className={refreshing ? 'h-4 w-4 animate-spin' : 'h-4 w-4'} />
            </IconButton>
            <ButtonLink href={href('/create')} variant="primary" icon={<Plus className="h-4 w-4" />}>
              Tạo video
            </ButtonLink>
          </>
        }
      />

      <div className="mb-4 flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
        <Tabs
          items={FILTERS.map((f) => ({ id: f.id, label: f.label, count: projects ? counts[f.id] : undefined }))}
          value={filter}
          onChange={setFilter}
          className="md:flex-1"
        />
        <div className="md:w-64 md:pb-1.5">
          <Input leading={<Search />} value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Tìm theo tiêu đề, nguồn…" />
        </div>
      </div>

      <Card className="overflow-hidden">
        {!projects ? (
          <div className="divide-y divide-line">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="flex items-center gap-4 px-4 py-3">
                <Skeleton className="h-14 w-8" />
                <div className="flex-1 space-y-2">
                  <Skeleton className="h-4 w-2/3" />
                  <Skeleton className="h-3 w-1/4" />
                </div>
              </div>
            ))}
          </div>
        ) : visible.length === 0 ? (
          list.length === 0 ? (
            <EmptyState
              icon={<Film />}
              title="Chưa có video nào"
              description="Dán link một bài báo để hệ thống tạo video đầu tiên cho bạn."
              action={
                <ButtonLink href={href('/create')} variant="primary" icon={<Plus className="h-4 w-4" />}>
                  Tạo video
                </ButtonLink>
              }
            />
          ) : (
            <EmptyState
              icon={<Search />}
              title="Không có video phù hợp"
              description="Thử đổi bộ lọc hoặc từ khoá tìm kiếm."
              action={
                <Button
                  onClick={() => {
                    setFilter('all');
                    setQuery('');
                  }}
                >
                  Xoá bộ lọc
                </Button>
              }
            />
          )
        ) : (
          <table className="w-full text-sm">
            <thead className="hidden border-b border-line bg-subtle text-left text-xs text-fg-muted md:table-header-group">
              <tr>
                <th className="py-2.5 pl-4 pr-3 font-medium">Video</th>
                <th className="px-3 py-2.5 font-medium">Trạng thái</th>
                <th className="px-3 py-2.5 text-right font-medium">Thời lượng</th>
                <th className="px-3 py-2.5 text-right font-medium">Cảnh</th>
                <th className="px-3 py-2.5 font-medium">Cập nhật</th>
                <th className="w-10" />
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {visible.slice(0, limit).map((p) => (
                <tr
                  key={p.id}
                  onClick={() => navigate(`/videos/${p.id}`)}
                  className="group cursor-pointer transition-colors hover:bg-subtle"
                >
                  <td className="py-3 pl-4 pr-3">
                    <div className="flex items-center gap-3">
                      <ReelThumb src={projectThumb(p)} className="w-8" />
                      <div className="min-w-0">
                        <a
                          href={href(`/videos/${p.id}`)}
                          onClick={(e) => e.stopPropagation()}
                          className="line-clamp-2 font-medium text-fg"
                        >
                          {p.title}
                        </a>
                        <div className="mt-0.5 truncate text-xs text-fg-muted">
                          {p.publisher || domainOf(p.sourceUrl)}
                          <span className="md:hidden"> · {timeAgo(p.updatedAtUtc ?? p.createdAtUtc)}</span>
                        </div>
                        <div className="mt-1.5 md:hidden">
                          <StatusBadge status={p.status} />
                        </div>
                      </div>
                    </div>
                  </td>
                  <td className="hidden px-3 py-3 md:table-cell">
                    <StatusBadge status={p.status} />
                  </td>
                  <td className="hidden px-3 py-3 text-right tabular text-fg-muted md:table-cell">
                    {formatClock(p.actualDurationSeconds ?? p.targetDurationSeconds)}
                  </td>
                  <td className="hidden px-3 py-3 text-right tabular text-fg-muted md:table-cell">
                    {p.sceneCount || p.scenes?.length || '—'}
                  </td>
                  <td className="hidden whitespace-nowrap px-3 py-3 text-fg-muted md:table-cell">
                    {timeAgo(p.updatedAtUtc ?? p.createdAtUtc)}
                  </td>
                  <td className="pr-3">
                    <ChevronRight className="h-4 w-4 text-fg-subtle transition-transform group-hover:translate-x-0.5" />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {visible.length > limit && (
          <div className="flex items-center justify-between border-t border-line px-4 py-3 text-[13px] text-fg-muted">
            <span>
              Đang hiện {limit}/{visible.length} video
            </span>
            <Button size="sm" onClick={() => setLimit((l) => l + PAGE)}>
              Xem thêm
            </Button>
          </div>
        )}
      </Card>
    </div>
  );
};
