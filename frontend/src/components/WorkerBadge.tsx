import { useEffect, useState } from 'react';
import { Cloud, Laptop, AlertTriangle, ArrowLeftRight } from 'lucide-react';
import { api, WorkerStatus } from '../services/api';
import { cn } from './ui';

/** Friendly names for the background jobs that show up as "đang làm". */
const JOB_NAMES: Record<string, string> = {
  RunGenerationAsync: 'tạo clip',
  RunComposeAsync: 'ghép video',
  RunPrepareImportAsync: 'chuẩn bị kịch bản',
  RunInstagramAsync: 'đăng Instagram',
  RunFacebookDraftAsync: 'gửi bản nháp Facebook',
  RunPublishAsync: 'đăng Facebook',
  RunMusicAiAsync: 'sáng tác nhạc',
  RunPlanAsync: 'lên phân cảnh',
  RunVoicesAsync: 'đọc lời bình',
  RunEffectsAsync: 'tạo tiếng động',
  RunRecoverAsync: 'lấy lại clip',
  SyncAsync: 'cập nhật số liệu',
  RunAsync: 'trả lời bình luận',
  ProduceVideoAsync: 'làm video tin tức',
  RefreshAsync: 'lấy tin hot',
};

const short = (v?: string | null) => (v ? v.slice(0, 7) : '?');

function ago(iso?: string | null, now?: string) {
  if (!iso) return 'chưa thấy';
  const s = Math.max(0, Math.round((new Date(now ?? Date.now()).getTime() - new Date(iso).getTime()) / 1000));
  return s < 60 ? `${s} giây trước` : s < 3600 ? `${Math.round(s / 60)} phút trước` : `${Math.round(s / 3600)} giờ trước`;
}

function jobs(list: string[]) {
  if (!list.length) return 'rảnh';
  return 'đang ' + list.map((j) => JOB_NAMES[j] ?? 'làm việc').join(', ');
}

/** Sidebar / header badge: whether the owner's PC or Railway runs the background jobs right now. */
export function WorkerBadge({ compact = false }: { compact?: boolean }) {
  const [st, setSt] = useState<WorkerStatus | null>(null);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    let alive = true;
    const load = () => api.getWorkerStatus().then((s) => alive && setSt(s));
    load();
    const t = window.setInterval(load, 20000);
    return () => {
      alive = false;
      window.clearInterval(t);
    };
  }, []);

  if (!st) return null;

  const view = {
    pc: { icon: Laptop, label: 'Máy tính', tone: 'text-success bg-success/10 border-success/30' },
    cloud: { icon: Cloud, label: 'Railway', tone: 'text-sky-500 bg-sky-500/10 border-sky-500/30' },
    both: { icon: ArrowLeftRight, label: 'Đang chuyển', tone: 'text-warning bg-warning/10 border-warning/30' },
    none: { icon: AlertTriangle, label: 'Không ai làm', tone: 'text-danger bg-danger/10 border-danger/30' },
  }[st.worker];
  const Icon = view.icon;
  const pcBehind = st.pc.version && st.pc.version !== st.webVersion;

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        title="Ai đang làm việc nền (ghép video, tạo clip, tự đăng)"
        className={cn('flex items-center gap-1.5 rounded-md border px-2 text-xs font-medium', compact ? 'h-7' : 'h-7 w-full', view.tone)}
      >
        <Icon className="h-3.5 w-3.5 shrink-0" />
        <span className="truncate">{compact ? view.label : `Việc nền: ${view.label}`}</span>
      </button>
      {open && (
        <div
          className={cn(
            'absolute z-50 w-72 rounded-lg border border-line bg-surface p-3 text-xs text-fg-muted shadow-pop',
            compact ? 'right-0 top-9' : 'bottom-9 left-0'
          )}
        >
          <div className="mb-2 font-medium text-fg">Ai đang làm việc nền?</div>
          <p className="mb-2">
            Trang web luôn do Railway phục vụ. Việc nặng (tạo clip, ghép video, tự đăng) do <b>máy tính</b> làm khi đang bật,
            tắt máy thì <b>Railway</b> tự làm.
          </p>
          <div className="space-y-1.5">
            <div className="flex items-start gap-2">
              <Laptop className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              <div>
                <b className="text-fg">Máy tính:</b>{' '}
                {st.pc.online ? (st.pc.working ? `đang nhận việc — ${jobs(st.pc.runningJobs)}` : 'bật nhưng chưa nhận việc') : 'tắt / chưa chạy'}
                <div className="text-fg-subtle">
                  tín hiệu {ago(st.pc.heartbeatUtc, st.nowUtc)} · bản {short(st.pc.version)}
                  {pcBehind ? ' (khác Railway — đang cập nhật)' : ''}
                </div>
              </div>
            </div>
            <div className="flex items-start gap-2">
              <Cloud className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              <div>
                <b className="text-fg">Railway:</b>{' '}
                {st.cloud.working ? `đang làm — ${jobs(st.cloud.runningJobs)}` : 'nhường cho máy tính, chỉ giữ trang web'}
                <div className="text-fg-subtle">
                  tín hiệu {ago(st.cloud.heartbeatUtc, st.nowUtc)} · bản {short(st.webVersion)}
                </div>
              </div>
            </div>
          </div>
          {st.worker === 'both' && (
            <p className="mt-2 text-warning">Railway đang làm nốt việc dở rồi sẽ nhường cho máy tính.</p>
          )}
          {st.worker === 'none' && (
            <p className="mt-2 text-danger">Không bên nào đang làm việc nền — đợi 1 phút; nếu vẫn vậy hãy báo lại.</p>
          )}
        </div>
      )}
    </div>
  );
}
