import { useEffect, useState } from 'react';
import { Button, Card, Field, Input, ProgressBar, Skeleton } from '../ui';
import { useToast } from '../feedback';
import { StudioCosts, studio, usd } from '../../services/studio';
import { navigate } from '../../lib/router';

const vnTime = (iso: string) => new Date(iso).toLocaleString('vi-VN', { hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit' });

/** Spending of the studio: month vs budget, per video, every paid call. */
export function CostPanel({ onAuthError }: { onAuthError: (e: unknown) => boolean }) {
  const toast = useToast();
  const [costs, setCosts] = useState<StudioCosts | null>(null);
  const [budget, setBudget] = useState('');
  const [saving, setSaving] = useState(false);

  const load = async () => {
    try {
      const c = await studio.costs();
      setCosts(c);
      setBudget(String(c.monthlyBudgetUsd));
    } catch (e: any) {
      toast.error(e.message);
    }
  };
  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const saveBudget = async () => {
    setSaving(true);
    try {
      await studio.setBudget(Number(budget) || 0);
      toast.success('Đã lưu trần ngân sách');
      await load();
    } catch (e) {
      if (!onAuthError(e)) toast.error((e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  if (!costs) return <Skeleton className="h-48 w-full" />;
  const pct = costs.monthlyBudgetUsd > 0 ? Math.min(100, (costs.monthSpentUsd / costs.monthlyBudgetUsd) * 100) : 0;

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Card className="p-3">
          <div className="text-xs text-fg-muted">Đã chi tháng này</div>
          <div className="mt-1 text-xl font-semibold text-fg">{usd(costs.monthSpentUsd)}</div>
          <div className="text-[11px] text-fg-subtle">{costs.monthVideoSeconds} giây video đã tạo</div>
        </Card>
        <Card className="p-3">
          <div className="text-xs text-fg-muted">Trần tháng</div>
          <div className="mt-1 text-xl font-semibold text-fg">{costs.monthlyBudgetUsd > 0 ? usd(costs.monthlyBudgetUsd) : 'Không giới hạn'}</div>
          <div className="text-[11px] text-fg-subtle">còn lại {costs.monthlyBudgetUsd > 0 ? usd(costs.monthRemainingUsd) : '—'}</div>
        </Card>
        <Card className="p-3">
          <div className="text-xs text-fg-muted">Tổng từ trước tới nay</div>
          <div className="mt-1 text-xl font-semibold text-fg">{usd(costs.allTimeSpentUsd)}</div>
        </Card>
        <Card className="p-3">
          <div className="text-xs text-fg-muted">Giá Omni / giây video</div>
          <div className="mt-1 space-y-0.5 text-[12px] text-fg">
            {Object.entries(costs.pricePerSecondUsd).map(([r, p]) => (
              <div key={r} className="flex justify-between">
                <span>{r}</span>
                <span>{usd(p, 3)}</span>
              </div>
            ))}
          </div>
        </Card>
      </div>

      {costs.monthlyBudgetUsd > 0 && <ProgressBar value={pct} tone={pct > 90 ? 'danger' : pct > 70 ? 'warning' : 'success'} />}

      <Card className="space-y-2 p-4">
        <div className="text-sm font-semibold text-fg">Trần ngân sách tháng cho Studio</div>
        <p className="text-xs text-fg-muted">
          Trước mỗi bước tốn tiền (lập kế hoạch, tạo clip), hệ thống kiểm tra trần này; vượt trần thì dừng và báo, không tự trừ tiếp. Đặt 0 nếu không
          muốn giới hạn. Gemini tính tiền vào thẻ/tài khoản Google Cloud gắn với khoá API.
        </p>
        <div className="flex max-w-sm items-end gap-2">
          <Field label="USD / tháng" className="flex-1">
            <Input type="number" min={0} step={1} value={budget} onChange={(e) => setBudget(e.target.value)} />
          </Field>
          <Button variant="primary" loading={saving} onClick={saveBudget}>
            Lưu
          </Button>
        </div>
      </Card>

      <Card className="p-4">
        <div className="mb-2 text-sm font-semibold text-fg">Chi phí từng video</div>
        {costs.videos.length === 0 ? (
          <p className="text-xs text-fg-muted">Chưa có video nào.</p>
        ) : (
          <ul className="divide-y divide-line text-xs">
            {costs.videos.map((v) => (
              <li key={v.id} className="flex items-center justify-between gap-2 py-1.5">
                <button type="button" className="truncate text-left text-fg hover:underline" onClick={() => navigate(`/studio/videos/${v.id}`)}>
                  {v.title}
                </button>
                <span className="shrink-0 font-medium text-fg">{usd(v.costUsd, 3)}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card className="p-4">
        <div className="mb-2 text-sm font-semibold text-fg">Lịch sử gọi AI (50 lần gần nhất)</div>
        {costs.recent.length === 0 ? (
          <p className="text-xs text-fg-muted">Chưa có.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[520px] text-left text-xs">
              <thead className="text-fg-subtle">
                <tr>
                  <th className="py-1 pr-2 font-medium">Lúc</th>
                  <th className="py-1 pr-2 font-medium">Loại</th>
                  <th className="py-1 pr-2 font-medium">Video</th>
                  <th className="py-1 pr-2 font-medium">Chi tiết</th>
                  <th className="py-1 text-right font-medium">Chi phí</th>
                </tr>
              </thead>
              <tbody>
                {costs.recent.map((r, i) => (
                  <tr key={i} className="border-t border-line">
                    <td className="py-1 pr-2 text-fg-muted">{vnTime(r.createdAtUtc)}</td>
                    <td className="py-1 pr-2">{r.kind === 'Video' ? 'Tạo clip' : 'Lập kế hoạch'}</td>
                    <td className="max-w-[160px] truncate py-1 pr-2 text-fg-muted">{r.videoTitle ?? '—'}</td>
                    <td className="py-1 pr-2 text-fg-muted">
                      {r.seconds ? `${r.seconds}s · ` : ''}
                      {r.note ?? r.model}
                    </td>
                    <td className="py-1 text-right font-medium">{usd(r.costUsd, 4)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  );
}
