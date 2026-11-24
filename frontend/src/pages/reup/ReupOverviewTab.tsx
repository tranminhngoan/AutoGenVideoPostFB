import { AlertTriangle, Clapperboard, Gauge, HardDrive, Plus, ShieldCheck } from 'lucide-react';
import { Alert, ButtonLink, Card, CardBody, CardHeader, EmptyState, ProgressBar, Skeleton, Thumb } from '../../components/ui';
import { href } from '../../lib/router';
import { timeAgo } from '../../lib/format';
import { reup } from '../../services/reup';
import { useReup } from './ReupPage';
import { JobStatusBadge, Stat, minutesText, seconds } from './ReupBits';

export function ReupOverviewTab() {
  const { overview: o } = useReup();
  if (!o) return <Skeleton className="h-64 w-full" />;
  const e = o.engine;
  const fps720 = e.backgroundFps?.['720p'];
  const fps1080 = e.backgroundFps?.['1080p'];
  // a 60 s video at 30 fps; ~15 % on top for decoding / encoding / audio / checks
  const eta = (fps?: number) => (fps ? minutesText((1800 / fps) * 1.15) : '—');

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Stat label="Đã xử lý xong" value={o.done} hint={`${o.processedMinutes} phút video`} tone="success" />
        <Stat label="Đang xử lý" value={o.running} hint={o.queued ? `${o.queued} video đang chờ` : 'Không có video chờ'} tone="info" />
        <Stat label="Lỗi" value={o.failed} hint="Xem lý do trong Lịch sử" tone={o.failed ? 'danger' : 'neutral'} />
        <Stat label="Kho file đang dùng" value={`${o.storageMb.toFixed(0)} MB`} hint="Miễn phí tới 10 GB (R2)" />
      </div>

      {!o.pcOnline && o.pcUpdating && (
        <Alert tone="warning" icon={<AlertTriangle />} title="Máy tính đang cập nhật bản mới">
          Máy tính đang bật nhưng chạy bản cũ hơn Railway, nên tạm chưa nhận việc. Cửa sổ <code>start-pc-worker</code> tự cập nhật trong vài phút
          (build lần đầu mất 2–4 phút). Nếu quá 10 phút: mở cửa sổ đen đó, bấm <b>Esc</b> (tiêu đề có chữ "Select" nghĩa là đang bị tạm dừng) hoặc
          chạy lại <code>start-pc-worker.bat</code>.
        </Alert>
      )}
      {!o.pcOnline && !o.pcUpdating && (
        <Alert tone="warning" icon={<AlertTriangle />} title="Máy tính đang tắt">
          Video chỉ được xử lý trên máy tính (không tốn CPU Railway). Bật máy và chạy <code>scripts\pc\start-pc-worker.bat</code> — video đang chờ sẽ tự chạy.
        </Alert>
      )}
      {o.pcOnline && !e.ready && !e.checkedAtUtc && (
        <Alert tone="info" icon={<Gauge />} title="Đang chờ máy tính kiểm tra engine">
          Máy tính tự kiểm tra engine và đo tốc độ khoảng 1 phút sau khi bật / cập nhật — trang tự làm mới, không cần làm gì.
        </Alert>
      )}
      {o.pcOnline && !e.ready && e.checkedAtUtc && (
        <Alert tone="danger" icon={<AlertTriangle />} title="Engine trên máy tính chưa sẵn sàng">
          {e.error ?? 'Chưa cài.'} — chạy <code>scripts\pc\setup-reup-engine.bat</code> một lần (tải ~55 MB model miễn phí), sau đó chờ vài phút.
        </Alert>
      )}

      <div className="grid gap-5 lg:grid-cols-[1.2fr_1fr]">
        <Card>
          <CardHeader
            title="Video gần đây"
            actions={
              <ButtonLink href={href('/reup/new')} variant="primary" size="sm" icon={<Plus className="h-4 w-4" />}>
                Tạo video
              </ButtonLink>
            }
          />
          <CardBody className="space-y-2">
            {o.recent.length === 0 ? (
              <EmptyState icon={<Clapperboard />} title="Chưa có video nào" description="Tải video nguồn lên, chọn cảnh nền / nhân vật rồi bấm Bắt đầu." />
            ) : (
              o.recent.map((j) => (
                <a key={j.id} href={href(`/reup/jobs/${j.id}`)} className="flex items-center gap-3 rounded-lg border border-line p-2 transition-colors hover:bg-muted/60">
                  <Thumb src={j.hasSourceThumb ? reup.thumbUrl(j.id) : null} className="h-14 w-10 shrink-0 rounded-md" />
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-sm font-medium text-fg">{j.title}</div>
                    <div className="text-xs text-fg-muted">
                      {seconds(j.sourceDurationSeconds)} · {j.outputWidth}×{j.outputHeight} · {timeAgo(j.createdAtUtc)}
                    </div>
                    {(j.status === 'Processing' || j.status === 'Queued') && <ProgressBar value={j.progressPercent} className="mt-1.5" />}
                  </div>
                  <JobStatusBadge job={j} />
                </a>
              ))
            )}
          </CardBody>
        </Card>

        <div className="space-y-5">
          <Card>
            <CardHeader title="Máy tính xử lý" description={e.machine ? `${e.machine} · ${e.cpuThreads} luồng CPU · ${e.ramGb} GB RAM` : 'Chưa nhận được báo cáo từ máy tính.'} />
            <CardBody className="space-y-3 text-[13px]">
              <div className="flex items-center gap-2">
                <Gauge className="h-4 w-4 text-accent" />
                <span className="text-fg-muted">Tốc độ thay nền đo trên máy:</span>
              </div>
              <div className="grid grid-cols-2 gap-2">
                <div className="rounded-md bg-subtle px-3 py-2">
                  <div className="text-xs text-fg-muted">720p</div>
                  <div className="font-semibold text-fg">{fps720 ? `${fps720} khung/giây` : 'chưa đo'}</div>
                  <div className="text-xs text-fg-subtle">60s video: {eta(fps720)}</div>
                </div>
                <div className="rounded-md bg-subtle px-3 py-2">
                  <div className="text-xs text-fg-muted">1080p</div>
                  <div className="font-semibold text-fg">{fps1080 ? `${fps1080} khung/giây` : 'chưa đo'}</div>
                  <div className="text-xs text-fg-subtle">60s video: {eta(fps1080)}</div>
                </div>
              </div>
              <p className="text-xs text-fg-subtle">
                Số đo thật trên CPU của máy (không có thay mặt). Thay mặt cần thêm thời gian; thời gian thực tế hiện trong lúc xử lý.
              </p>
              {e.freeDiskGb != null && (
                <div className="flex items-center gap-2 text-xs text-fg-muted">
                  <HardDrive className="h-3.5 w-3.5" /> Ổ đĩa tạm còn trống {e.freeDiskGb} GB
                </div>
              )}
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Thay khuôn mặt" />
            <CardBody className="text-[13px]">
              {o.faceSwapAvailable ? (
                <div className="flex items-start gap-2 text-success">
                  <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0" /> Đã có model thay mặt kèm giấy phép thương mại.
                </div>
              ) : (
                <div className="space-y-2 text-fg-muted">
                  <p>{o.faceSwapBlockedReason}</p>
                  <a className="text-accent hover:underline" href={href('/reup/settings')}>
                    Xem hướng dẫn trong Cài đặt →
                  </a>
                </div>
              )}
            </CardBody>
          </Card>
        </div>
      </div>
    </div>
  );
}
