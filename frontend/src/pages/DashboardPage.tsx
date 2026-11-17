import React, { useEffect, useState } from 'react';
import { ArrowRight, Link2, ChevronRight, AlertTriangle, Clock3, Inbox, Flame } from 'lucide-react';
import { api, AutoPostOverviewDto, SystemMetricsDto, SystemCostSummaryDto, TrendingArticleDto } from '../services/api';
import { AutoPostPageCard, AutoPostTotals, isActiveRun } from '../components/AutoPostWidgets';
import { AiCostCard } from '../components/AiCostCard';
import { PageViewStats } from '../components/PageViewStats';
import { VideoProject } from '../types';
import {
  Button,
  Card,
  CardHeader,
  CardBody,
  Input,
  PageHeader,
  ProgressBar,
  Skeleton,
  EmptyState,
  Dot,
  cn,
} from '../components/ui';
import { StatusBadge, ReelThumb, projectThumb } from '../components/VideoBits';
import { useToast } from '../components/feedback';
import { createVideoFromUrl } from '../lib/actions';
import { PagePickerModal } from '../components/PagePickerModal';
import { href, navigate } from '../lib/router';
import { formatUsd, formatNumber, timeAgo, isValidUrl, errorText, domainOf, statusMeta } from '../lib/format';

interface DashboardPageProps {
  projects: VideoProject[] | null;
  onCreated: () => void;
  apiHealthy: boolean | null;
}

function todayLabel() {
  const s = new Date().toLocaleDateString('vi-VN', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  return s.charAt(0).toUpperCase() + s.slice(1);
}

export const DashboardPage: React.FC<DashboardPageProps> = ({ projects, onCreated, apiHealthy }) => {
  const toast = useToast();
  const [url, setUrl] = useState('');
  const [creating, setCreating] = useState(false);
  const [metrics, setMetrics] = useState<SystemMetricsDto | null>(null);
  const [costs, setCosts] = useState<SystemCostSummaryDto | null>(null);
  const [statsLoaded, setStatsLoaded] = useState(false);
  const [hot, setHot] = useState<TrendingArticleDto[] | null>(null);
  const [autoPost, setAutoPost] = useState<AutoPostOverviewDto | null>(null);

  // Every Facebook Page's auto-posting at a glance (faster refresh while videos are being made)
  useEffect(() => {
    let alive = true;
    let timer: ReturnType<typeof setTimeout>;
    const loadAutoPost = async () => {
      const data = await api.getAutoPost().catch(() => null);
      if (!alive) return;
      setAutoPost(data);
      timer = setTimeout(loadAutoPost, data?.recentRuns.some(isActiveRun) ? 10000 : 30000);
    };
    loadAutoPost();
    return () => {
      alive = false;
      clearTimeout(timer);
    };
  }, []);

  useEffect(() => {
    const loadHot = () =>
      api
        .getTrending({ hours: 24, limit: 5 })
        .then((d) => setHot(d.items))
        .catch(() => setHot([]));
    loadHot();
    const t = setInterval(loadHot, 5 * 60 * 1000);
    return () => clearInterval(t);
  }, []);

  useEffect(() => {
    const load = async () => {
      const [m, c] = await Promise.all([
        api.getSystemMetrics().catch(() => null),
        api.getSystemCosts().catch(() => null),
      ]);
      setMetrics(m);
      setCosts(c);
      setStatsLoaded(true);
    };
    load();
    const t = setInterval(load, 30000);
    return () => clearInterval(t);
  }, []);

  // The pasted link first asks which page the video is for
  const [picking, setPicking] = useState(false);
  const handleQuickCreate = (e: React.FormEvent) => {
    e.preventDefault();
    if (!isValidUrl(url)) {
      toast.error('Link chưa hợp lệ', 'Hãy dán đường dẫn đầy đủ, bắt đầu bằng https://');
      return;
    }
    setPicking(true);
  };

  const createFor = async (facebookAccountId: string) => {
    setCreating(true);
    try {
      const project = await createVideoFromUrl({ url, facebookAccountId });
      setPicking(false);
      toast.success('Đã bắt đầu tạo video', 'Hệ thống đang đọc bài và viết kịch bản.');
      onCreated();
      navigate(`/videos/${project.id}`);
    } catch (err) {
      toast.error('Không tạo được video', errorText(err, 'Không kết nối được máy chủ.'));
    } finally {
      setCreating(false);
    }
  };

  const list = projects ?? [];
  const needsAttention = list
    .filter((p) => p.status === 'AwaitingApproval' || p.status === 'Failed')
    .slice(0, 5);
  const inProgress = list.filter((p) => statusMeta(p.status).group === 'processing');
  const recent = list.slice(0, 6);
  const awaiting = list.filter((p) => p.status === 'AwaitingApproval').length;

  return (
    <div>
      <PageHeader title="Tổng quan" description={todayLabel()} />

      {/* Quick create */}
      <Card className="mb-6">
        <form onSubmit={handleQuickCreate} className="p-5">
          <label htmlFor="quick-url" className="text-[15px] font-semibold text-fg">
            Tạo video từ bài báo
          </label>
          <p className="mt-0.5 text-[13px] text-fg-muted">
            Dán link bài viết (trong nước hoặc quốc tế), hệ thống sẽ đọc bài, viết kịch bản tiếng Việt và dựng video dọc theo độ dài nội dung.
          </p>
          <div className="mt-4 flex flex-col gap-2 sm:flex-row">
            <div className="flex-1">
              <Input
                id="quick-url"
                leading={<Link2 />}
                value={url}
                onChange={(e) => setUrl(e.target.value)}
                placeholder="https://vnexpress.net/..."
                className="h-10"
                disabled={creating}
              />
            </div>
            <Button type="submit" variant="primary" size="lg" loading={creating} disabled={!url.trim()}>
              Tạo video
            </Button>
          </div>
          <div className="mt-3 text-xs text-fg-subtle">
            Dùng giọng đọc và AI mặc định trong{' '}
            <a href={href('/settings')} className="text-fg-muted underline-offset-2 hover:text-fg hover:underline">
              Cài đặt
            </a>
            . Cần chỉnh riêng?{' '}
            <a href={href('/create')} className="text-fg-muted underline-offset-2 hover:text-fg hover:underline">
              Mở trình tạo đầy đủ
            </a>
          </div>
        </form>
      </Card>

      {/* Stats */}
      <div className="mb-6 grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-line bg-line lg:grid-cols-4">
        <Stat label="Tổng số video" value={statsLoaded ? formatNumber(costs?.totalProjectsCount ?? list.length) : null} />
        <Stat label="Đã đăng Facebook" value={statsLoaded ? formatNumber(costs?.publishedProjectsCount ?? 0) : null} />
        <Stat
          label="Đang chờ duyệt"
          value={projects ? formatNumber(awaiting) : null}
          hint={awaiting > 0 ? <a href={href('/videos?filter=review')} className="hover:text-fg">Xem ngay</a> : undefined}
        />
        <Stat
          label="Chi phí trung bình / video"
          value={statsLoaded ? formatUsd(costs?.averageCostPerProjectUsd) : null}
          hint={costs ? `Tổng ${formatUsd(costs.totalCostUsd, 2)}` : undefined}
        />
      </div>

      {/* Auto-posting across all Facebook Pages */}
      {autoPost && (
        <section className="mb-6 space-y-3">
          <div className="flex items-end justify-between gap-3">
            <div>
              <h2 className="text-[15px] font-semibold text-fg">Các Page tự động đăng</h2>
              <p className="text-[13px] text-fg-muted">Mỗi Page một chủ đề, tự chọn tin hot, làm video và đăng theo lịch.</p>
            </div>
            <a href={href('/auto-post')} className="inline-flex shrink-0 items-center gap-1 text-[13px] text-fg-muted hover:text-fg">
              Quản lý <ChevronRight className="h-4 w-4" />
            </a>
          </div>
          {autoPost.pages.length === 0 ? (
            <Card className="flex flex-col items-start gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
              <p className="text-[13px] text-fg-muted">Chưa kết nối Page Facebook nào. Mỗi Page dùng một token riêng và một chủ đề riêng.</p>
              <Button size="sm" onClick={() => navigate('/settings/facebook')}>
                Kết nối Page
              </Button>
            </Card>
          ) : (
            <>
              <AutoPostTotals totals={autoPost.totals} />
              <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                {autoPost.pages.map((p) => (
                  <AutoPostPageCard key={p.facebookAccountId} page={p} onOpen={() => navigate(`/auto-post?page=${p.facebookAccountId}`)} />
                ))}
              </div>
            </>
          )}
        </section>
      )}

      {/* Reels views of every Facebook Page */}
      <PageViewStats />

      <AiCostCard />

      <PagePickerModal open={picking} creating={creating} onClose={() => setPicking(false)} onConfirm={createFor} />

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <div className="min-w-0 space-y-6 lg:col-span-2">
          {/* Needs attention */}
          <Card>
            <CardHeader title="Cần bạn xử lý" description="Video chờ duyệt hoặc gặp lỗi." />
            <CardBody className="pt-3">
              {!projects ? (
                <ListSkeleton rows={3} />
              ) : needsAttention.length === 0 ? (
                <div className="flex items-center gap-3 rounded-md border border-dashed border-line px-4 py-5 text-[13px] text-fg-muted">
                  <Inbox className="h-4 w-4 text-fg-subtle" />
                  Không có việc tồn đọng.
                  {inProgress.length > 0 && ` ${inProgress.length} video đang được xử lý.`}
                </div>
              ) : (
                <ul className="-mx-2">
                  {needsAttention.map((p) => (
                    <ProjectRow key={p.id} project={p} />
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>

          {/* Recent */}
          <Card>
            <CardHeader
              title="Video gần đây"
              actions={
                <a href={href('/videos')} className="flex items-center gap-1 text-[13px] text-fg-muted hover:text-fg">
                  Xem tất cả <ArrowRight className="h-3.5 w-3.5" />
                </a>
              }
            />
            <CardBody className="pt-3">
              {!projects ? (
                <ListSkeleton rows={4} />
              ) : recent.length === 0 ? (
                <EmptyState
                  title="Chưa có video nào"
                  description="Dán link bài báo ở ô phía trên để tạo video đầu tiên."
                  className="py-8"
                />
              ) : (
                <ul className="-mx-2">
                  {recent.map((p) => (
                    <ProjectRow key={p.id} project={p} />
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </div>

        {/* Hot news + System */}
        <div className="min-w-0 space-y-6">
          <Card>
            <CardHeader
              title="Tin đang hot"
              actions={
                <a href={href('/trending')} className="flex items-center gap-1 text-[13px] text-fg-muted hover:text-fg">
                  Xem tất cả <ArrowRight className="h-3.5 w-3.5" />
                </a>
              }
            />
            <CardBody className="pt-2">
              {!hot ? (
                <ListSkeleton rows={3} />
              ) : hot.length === 0 ? (
                <p className="py-3 text-[13px] text-fg-muted">Chưa có dữ liệu. Worker sẽ tự lấy tin trong vài phút.</p>
              ) : (
                <ul className="-mx-2">
                  {hot.map((t) => (
                    <li key={t.id}>
                      <a
                        href={href(`/create?url=${encodeURIComponent(t.url)}`)}
                        className="flex gap-2.5 rounded-md px-2 py-2 transition-colors hover:bg-muted/70"
                        title="Tạo video từ tin này"
                      >
                        <span className="flex w-8 shrink-0 items-center gap-0.5 pt-0.5 text-xs font-semibold tabular text-danger">
                          <Flame className="h-3.5 w-3.5" />
                          {Math.round(t.hotScore)}
                        </span>
                        <span className="min-w-0">
                          <span className="line-clamp-2 text-[13px] font-medium leading-snug text-fg">{t.title}</span>
                          <span className="mt-0.5 block text-xs text-fg-muted">
                            {t.sourceName}
                            {t.coverageCount > 1 && ` · ${t.coverageCount} báo cùng đưa`}
                          </span>
                        </span>
                      </a>
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              title="Hệ thống"
              actions={
                <a href={href('/settings/system')} className="text-[13px] text-fg-muted hover:text-fg">
                  Chi tiết
                </a>
              }
            />
            <CardBody className="space-y-4 pt-3 text-[13px]">
              <SysRow
                label="Máy chủ"
                value={
                  <span className="flex items-center gap-1.5">
                    <Dot tone={apiHealthy ? 'success' : apiHealthy === null ? 'neutral' : 'danger'} />
                    {apiHealthy ? 'Hoạt động' : apiHealthy === null ? 'Đang kiểm tra' : 'Mất kết nối'}
                  </span>
                }
              />
              <SysRow
                label="Cơ sở dữ liệu"
                value={metrics ? `${metrics.databaseStatus} · ${metrics.databaseLatencyMs} ms` : '—'}
              />
              <SysRow
                label="Tác vụ"
                value={
                  metrics
                    ? `${metrics.activeJobsCount} đang chạy · ${metrics.pendingJobsCount} chờ`
                    : '—'
                }
              />
              {metrics && metrics.failedJobsCount > 0 && (
                <a
                  href={href('/activity?filter=failed')}
                  className="flex items-center gap-2 rounded-md bg-danger/[0.06] px-3 py-2 text-danger hover:bg-danger/10"
                >
                  <AlertTriangle className="h-3.5 w-3.5" />
                  {metrics.failedJobsCount} tác vụ lỗi
                  <ChevronRight className="ml-auto h-3.5 w-3.5" />
                </a>
              )}
              <div>
                <div className="mb-1.5 flex items-center justify-between">
                  <span className="text-fg-muted">Bộ nhớ tạm</span>
                  <span className="tabular text-fg">
                    {metrics ? `${metrics.tempStorageUsedMb} / ${metrics.tempStorageLimitMb} MB` : '—'}
                  </span>
                </div>
                <ProgressBar
                  value={metrics?.tempStorageUsagePercent ?? 0}
                  tone={(metrics?.tempStorageUsagePercent ?? 0) > 80 ? 'danger' : 'neutral'}
                />
              </div>
              <SysRow label="RAM" value={metrics ? `${metrics.memoryWorkingSetMb} MB` : '—'} />
              <SysRow label="Thời gian chạy" value={metrics?.uptime ? metrics.uptime.split('.')[0] : '—'} />
            </CardBody>
          </Card>
        </div>
      </div>
    </div>
  );
};

function Stat({ label, value, hint }: { label: string; value: React.ReactNode | null; hint?: React.ReactNode }) {
  return (
    <div className="bg-surface px-5 py-4">
      <div className="text-[13px] text-fg-muted">{label}</div>
      <div className="mt-1.5 text-2xl font-semibold tabular text-fg">
        {value === null ? <Skeleton className="h-7 w-16" /> : value}
      </div>
      <div className="mt-1 h-4 text-xs text-fg-subtle">{hint}</div>
    </div>
  );
}

function SysRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <span className="text-fg-muted">{label}</span>
      <span className="truncate text-right tabular text-fg">{value}</span>
    </div>
  );
}

function ProjectRow({ project: p }: { project: VideoProject }) {
  return (
    <li>
      <a
        href={href(`/videos/${p.id}`)}
        className="group flex items-center gap-3 rounded-md px-2 py-2 transition-colors hover:bg-muted/70"
      >
        <ReelThumb src={projectThumb(p)} className="w-7" />
        <div className="min-w-0 flex-1">
          <div className="line-clamp-2 text-sm font-medium leading-snug text-fg sm:line-clamp-1">{p.title}</div>
          <div className="mt-0.5 flex items-center gap-1.5 truncate text-xs text-fg-muted">
            <span className="truncate">{p.publisher || domainOf(p.sourceUrl)}</span>
            <span className="text-fg-subtle">·</span>
            <Clock3 className="h-3 w-3 shrink-0 text-fg-subtle" />
            <span className="shrink-0">{timeAgo(p.updatedAtUtc ?? p.createdAtUtc)}</span>
          </div>
          {/* Phones: status under the title so the title keeps the width */}
          <div className="mt-1.5 sm:hidden">
            <StatusBadge status={p.status} />
          </div>
        </div>
        <span className="hidden sm:block">
          <StatusBadge status={p.status} />
        </span>
        <ChevronRight className={cn('h-4 w-4 shrink-0 text-fg-subtle transition-transform group-hover:translate-x-0.5')} />
      </a>
    </li>
  );
}

function ListSkeleton({ rows }: { rows: number }) {
  return (
    <div className="space-y-3">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="flex items-center gap-3">
          <Skeleton className="h-12 w-7" />
          <div className="flex-1 space-y-1.5">
            <Skeleton className="h-4 w-3/4" />
            <Skeleton className="h-3 w-1/3" />
          </div>
        </div>
      ))}
    </div>
  );
}
