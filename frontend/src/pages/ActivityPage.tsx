import React, { useEffect, useMemo, useState } from 'react';
import { History, RefreshCw, RotateCcw, Square } from 'lucide-react';
import { api } from '../services/api';
import { ProjectJob, VideoProject } from '../types';
import { Badge, Button, Card, EmptyState, IconButton, PageHeader, ProgressBar, Skeleton, Tabs, cn } from '../components/ui';
import { useConfirm, useToast } from '../components/feedback';
import { href, useRoute } from '../lib/router';
import { elapsed, errorText, formatDateTime, isJobActive, jobStatusMeta, statusMeta } from '../lib/format';

type Filter = 'all' | 'active' | 'failed';

interface Row extends ProjectJob {
  projectTitle: string;
}

interface ActivityPageProps {
  projects: VideoProject[] | null;
  onRefresh: () => Promise<unknown>;
}

export const ActivityPage: React.FC<ActivityPageProps> = ({ projects, onRefresh }) => {
  const toast = useToast();
  const confirm = useConfirm();
  const route = useRoute();
  const [filter, setFilter] = useState<Filter>(route.query.get('filter') === 'failed' ? 'failed' : 'all');
  const [refreshing, setRefreshing] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [limit, setLimit] = useState(30);

  useEffect(() => setLimit(30), [filter]);

  const rows: Row[] = useMemo(() => {
    const all = (projects ?? []).flatMap((p) => (p.jobs ?? []).map((j) => ({ ...j, projectTitle: p.title })));
    return all.sort((a, b) => (b.startedAtUtc ?? '').localeCompare(a.startedAtUtc ?? ''));
  }, [projects]);

  const counts = {
    all: rows.length,
    active: rows.filter((r) => isJobActive(r.status)).length,
    failed: rows.filter((r) => r.status === 'Failed').length,
  };
  const visible = rows.filter((r) =>
    filter === 'all' ? true : filter === 'active' ? isJobActive(r.status) : r.status === 'Failed'
  );

  const refresh = async () => {
    setRefreshing(true);
    await onRefresh();
    setRefreshing(false);
  };

  const retry = async (r: Row) => {
    setBusy(r.id);
    try {
      await api.processVideoProject(r.videoProjectId);
      toast.success('Đã đưa video vào hàng đợi xử lý lại');
      await onRefresh();
    } catch (err) {
      toast.error('Không chạy lại được', errorText(err, ''));
    } finally {
      setBusy(null);
    }
  };

  const cancel = async (r: Row) => {
    const ok = await confirm({
      title: 'Dừng tác vụ này?',
      description: `Tác vụ đang xử lý “${r.projectTitle}” sẽ bị huỷ.`,
      confirmLabel: 'Dừng tác vụ',
      danger: true,
    });
    if (!ok) return;
    setBusy(r.id);
    const done = await api.cancelJob(r.id);
    setBusy(null);
    if (done) {
      toast.success('Đã dừng tác vụ');
      await onRefresh();
    } else {
      toast.error('Không dừng được tác vụ');
    }
  };

  return (
    <div>
      <PageHeader
        title="Hoạt động"
        description="Các tác vụ xử lý video chạy nền: đọc bài, viết kịch bản, dựng và đăng."
        actions={
          <IconButton label="Làm mới" onClick={refresh} className="border border-line-strong bg-surface shadow-card">
            <RefreshCw className={cn('h-4 w-4', refreshing && 'animate-spin')} />
          </IconButton>
        }
      />

      <Tabs
        className="mb-4"
        value={filter}
        onChange={setFilter}
        items={[
          { id: 'all', label: 'Tất cả', count: projects ? counts.all : undefined },
          { id: 'active', label: 'Đang chạy', count: projects ? counts.active : undefined },
          { id: 'failed', label: 'Lỗi', count: projects ? counts.failed : undefined },
        ]}
      />

      <Card className="overflow-hidden">
        {!projects ? (
          <div className="space-y-4 p-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : visible.length === 0 ? (
          <EmptyState
            icon={<History />}
            title={filter === 'failed' ? 'Không có tác vụ lỗi' : filter === 'active' ? 'Không có tác vụ nào đang chạy' : 'Chưa có hoạt động'}
            description={filter === 'all' ? 'Tác vụ sẽ xuất hiện ở đây khi bạn tạo video.' : undefined}
          />
        ) : (
          <div className="overflow-x-auto">
            {/* Phones: one card per job */}
            <ul className="divide-y divide-line md:hidden">
              {visible.slice(0, limit).map((r) => {
                const meta = jobStatusMeta(r.status);
                const active = isJobActive(r.status);
                return (
                  <li key={r.id} className="space-y-2 px-4 py-3.5">
                    <div className="flex items-start justify-between gap-3">
                      <a href={href(`/videos/${r.videoProjectId}`)} className="line-clamp-2 text-sm font-medium leading-snug text-fg">
                        {r.projectTitle}
                      </a>
                      <Badge tone={meta.tone} dot pulse={active && meta.tone === 'info'} className="shrink-0">
                        {meta.label}
                      </Badge>
                    </div>
                    <div className="flex flex-wrap gap-x-2 text-xs text-fg-muted">
                      <span>{statusMeta(r.step).label}</span>
                      <span className="text-fg-subtle">·</span>
                      <span>{formatDateTime(r.startedAtUtc)}</span>
                      {r.startedAtUtc && (
                        <>
                          <span className="text-fg-subtle">·</span>
                          <span>{elapsed(r.startedAtUtc, r.finishedAtUtc)}</span>
                        </>
                      )}
                    </div>
                    <div className="flex items-center gap-2">
                      <ProgressBar
                        value={r.progressPercentage}
                        tone={r.status === 'Failed' ? 'danger' : meta.tone === 'success' ? 'success' : 'info'}
                      />
                      <span className="w-11 shrink-0 whitespace-nowrap text-right text-xs tabular text-fg-subtle">{r.progressPercentage}%</span>
                    </div>
                    {r.errorMessage && r.status !== 'Completed' && <p className="line-clamp-3 text-xs text-danger">{r.errorMessage}</p>}
                    {(r.status === 'Failed' || active) && (
                      <div className="flex justify-end">
                        {r.status === 'Failed' && (
                          <Button size="sm" icon={<RotateCcw className="h-3.5 w-3.5" />} loading={busy === r.id} onClick={() => retry(r)}>
                            Chạy lại
                          </Button>
                        )}
                        {active && (
                          <Button size="sm" variant="danger-soft" icon={<Square className="h-3 w-3" />} loading={busy === r.id} onClick={() => cancel(r)}>
                            Dừng
                          </Button>
                        )}
                      </div>
                    )}
                  </li>
                );
              })}
            </ul>

            <table className="hidden w-full text-[13px] md:table">
              <thead className="border-b border-line bg-subtle text-left text-xs text-fg-muted">
                <tr>
                  <th className="py-2.5 pl-4 pr-3 font-medium">Video</th>
                  <th className="px-3 py-2.5 font-medium">Bước</th>
                  <th className="px-3 py-2.5 font-medium">Trạng thái</th>
                  <th className="w-36 px-3 py-2.5 font-medium">Tiến độ</th>
                  <th className="px-3 py-2.5 font-medium">Bắt đầu</th>
                  <th className="px-3 py-2.5 font-medium">Thời gian</th>
                  <th className="py-2.5 pl-3 pr-4" />
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {visible.slice(0, limit).map((r) => {
                  const meta = jobStatusMeta(r.status);
                  const active = isJobActive(r.status);
                  return (
                    <tr key={r.id} className="align-top">
                      <td className="max-w-[320px] py-3 pl-4 pr-3">
                        <a href={href(`/videos/${r.videoProjectId}`)} className="line-clamp-1 font-medium text-fg hover:underline">
                          {r.projectTitle}
                        </a>
                        {r.errorMessage && r.status !== 'Completed' && (
                          <p className="mt-0.5 line-clamp-2 text-xs text-danger" title={r.errorMessage}>
                            {r.errorMessage}
                          </p>
                        )}
                        {r.retryCount > 0 && <p className="mt-0.5 text-xs text-fg-subtle">Đã thử lại {r.retryCount} lần</p>}
                      </td>
                      <td className="whitespace-nowrap px-3 py-3 text-fg-muted">{statusMeta(r.step).label}</td>
                      <td className="px-3 py-3">
                        <Badge tone={meta.tone} dot pulse={active && meta.tone === 'info'}>
                          {meta.label}
                        </Badge>
                      </td>
                      <td className="px-3 py-3.5">
                        <div className="flex items-center gap-2">
                          <ProgressBar
                            value={r.progressPercentage}
                            tone={r.status === 'Failed' ? 'danger' : meta.tone === 'success' ? 'success' : 'info'}
                          />
                          <span className="w-10 shrink-0 whitespace-nowrap text-right text-xs tabular text-fg-subtle">{r.progressPercentage}%</span>
                        </div>
                      </td>
                      <td className="whitespace-nowrap px-3 py-3 text-fg-muted">{formatDateTime(r.startedAtUtc)}</td>
                      <td className="whitespace-nowrap px-3 py-3 text-fg-muted">
                        {r.startedAtUtc ? elapsed(r.startedAtUtc, r.finishedAtUtc) : '—'}
                      </td>
                      <td className="whitespace-nowrap py-2 pl-3 pr-4 text-right">
                        {r.status === 'Failed' && (
                          <Button size="sm" variant="ghost" icon={<RotateCcw className="h-3.5 w-3.5" />} loading={busy === r.id} onClick={() => retry(r)}>
                            Chạy lại
                          </Button>
                        )}
                        {active && (
                          <Button size="sm" variant="danger-soft" icon={<Square className="h-3 w-3" />} loading={busy === r.id} onClick={() => cancel(r)}>
                            Dừng
                          </Button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
            {visible.length > limit && (
              <div className="flex items-center justify-between border-t border-line px-4 py-3 text-[13px] text-fg-muted">
                <span>
                  Đang hiện {limit}/{visible.length} tác vụ
                </span>
                <Button size="sm" onClick={() => setLimit((l) => l + 30)}>
                  Xem thêm
                </Button>
              </div>
            )}
          </div>
        )}
      </Card>
    </div>
  );
};
