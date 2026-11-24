import { useEffect, useState } from 'react';
import { Download, History, Trash2 } from 'lucide-react';
import { Button, ButtonLink, Card, EmptyState, ProgressBar, Segmented, Skeleton, Thumb } from '../../components/ui';
import { useConfirm } from '../../components/feedback';
import { href } from '../../lib/router';
import { formatBytes, formatDateTime } from '../../lib/format';
import { isActive, reup, ReupJob } from '../../services/reup';
import { useReup } from './ReupPage';
import { JobStatusBadge, seconds } from './ReupBits';

type Filter = 'all' | 'done' | 'active' | 'failed';

export function ReupHistoryTab() {
  const { fail, refreshOverview } = useReup();
  const confirm = useConfirm();
  const [jobs, setJobs] = useState<ReupJob[] | null>(null);
  const [filter, setFilter] = useState<Filter>('all');

  const load = () => reup.jobs().then(setJobs).catch((e) => fail(e, 'Không tải được lịch sử.'));
  useEffect(() => {
    load();
    const t = setInterval(load, 5000);
    return () => clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const remove = async (j: ReupJob) => {
    if (!(await confirm({ title: `Xoá "${j.title}"?`, description: 'Xoá video nguồn, bản xem thử và video kết quả khỏi kho file.', confirmLabel: 'Xoá', danger: true }))) return;
    try {
      await reup.deleteJob(j.id);
      load();
      refreshOverview();
    } catch (e) {
      fail(e);
    }
  };

  const shown = (jobs ?? []).filter((j) =>
    filter === 'done' ? j.status === 'Done' : filter === 'active' ? isActive(j.status) : filter === 'failed' ? j.status === 'Failed' || j.status === 'Cancelled' : true
  );

  return (
    <div className="space-y-4">
      <Segmented
        size="sm"
        value={filter}
        onChange={setFilter}
        options={[
          { value: 'all', label: 'Tất cả' },
          { value: 'done', label: 'Hoàn tất' },
          { value: 'active', label: 'Đang chạy / chờ' },
          { value: 'failed', label: 'Lỗi / huỷ' },
        ]}
      />
      {jobs == null ? (
        <Skeleton className="h-64 w-full" />
      ) : shown.length === 0 ? (
        <Card>
          <EmptyState icon={<History />} title="Chưa có video" />
        </Card>
      ) : (
        <Card className="divide-y divide-line">
          {shown.map((j) => (
            <div key={j.id} className="flex flex-wrap items-center gap-3 px-4 py-3">
              <a href={href(`/reup/jobs/${j.id}`)} className="flex min-w-0 flex-1 items-center gap-3">
                <Thumb src={j.hasSourceThumb ? reup.thumbUrl(j.id) : null} className="h-16 w-11 shrink-0 rounded-md" />
                <div className="min-w-0">
                  <div className="truncate text-sm font-medium text-fg">{j.title}</div>
                  <div className="text-xs text-fg-muted">
                    {seconds(j.sourceDurationSeconds)} · {j.outputWidth}×{j.outputHeight} ·{' '}
                    {[j.faceSwapEnabled && 'thay mặt', j.backgroundEnabled && 'thay nền', j.audioMode !== 'Remove' ? 'có âm thanh' : 'không âm thanh'].filter(Boolean).join(', ')}
                  </div>
                  <div className="text-[11px] text-fg-subtle">
                    {formatDateTime(j.createdAtUtc)}
                    {j.outputSizeBytes ? ` · ${formatBytes(j.outputSizeBytes)}` : ''}
                    {j.filesDeleted ? ' · file đã hết hạn' : j.expiresAtUtc ? ` · lưu tới ${formatDateTime(j.expiresAtUtc)}` : ''}
                  </div>
                  {isActive(j.status) && <ProgressBar value={j.progressPercent} className="mt-1 w-48" />}
                  {j.status === 'Failed' && j.errorMessage && <div className="mt-0.5 line-clamp-1 text-xs text-danger">{j.errorMessage}</div>}
                </div>
              </a>
              <JobStatusBadge job={j} />
              <div className="flex gap-1">
                {j.hasOutput && (
                  <ButtonLink href={reup.outputUrl(j.id, true, j.finishedAtUtc ?? undefined)} size="sm" variant="ghost" title="Tải xuống">
                    <Download className="h-4 w-4" />
                  </ButtonLink>
                )}
                <Button size="sm" variant="danger-soft" disabled={isActive(j.status)} onClick={() => remove(j)} title="Xoá">
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            </div>
          ))}
        </Card>
      )}
    </div>
  );
}
