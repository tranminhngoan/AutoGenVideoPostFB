import React, { useEffect, useState } from 'react';
import { Save } from 'lucide-react';
import { api, AiUsageReportDto } from '../services/api';
import { Alert, Button, Card, CardBody, CardHeader, Field, Input, ProgressBar, Select } from './ui';
import { useToast } from './feedback';
import { errorText } from '../lib/format';

const KIND: Record<string, string> = {
  tts: 'Đọc giọng',
  script: 'Viết kịch bản',
  editorial: 'Kiểm chứng + caption',
  digest: 'Bản tin tối',
};

const MODES = [
  { value: 'warn', label: 'Chỉ cảnh báo' },
  { value: 'edge', label: 'Chuyển giọng Edge miễn phí' },
  { value: 'pause', label: 'Tạm dừng tự đăng' },
];

const usd = (v: number) => `${v < 1 ? v.toFixed(3) : v.toFixed(2)} USD`;

/** Dashboard: real AI spend this month (from Gemini's token counts) against the monthly budget. */
export function AiCostCard() {
  const toast = useToast();
  const [data, setData] = useState<AiUsageReportDto | null>(null);
  const [budget, setBudget] = useState('20');
  const [mode, setMode] = useState('warn');
  const [saving, setSaving] = useState(false);

  const load = () =>
    api
      .getAiUsage()
      .then((d) => {
        setData(d);
        setBudget(String(d.budget.monthlyUsd));
        setMode(d.budget.mode);
      })
      .catch(() => setData(null));

  useEffect(() => {
    load();
  }, []);

  const save = async () => {
    setSaving(true);
    try {
      await api.setAiBudget({ monthlyUsd: Number(budget) || 0, mode });
      toast.success('Đã lưu ngân sách AI');
      await load();
    } catch (err) {
      toast.error('Không lưu được', errorText(err, ''));
    } finally {
      setSaving(false);
    }
  };

  if (!data) return null;
  const b = data.budget;
  const pct = b.monthlyUsd > 0 ? Math.min(100, (b.spentUsd / b.monthlyUsd) * 100) : 0;
  const tone = b.overBudget ? 'danger' : pct >= 80 ? 'warning' : 'success';
  const dirty = Number(budget) !== b.monthlyUsd || mode !== b.mode;

  return (
    <Card className="mb-6">
      <CardHeader
        title="Chi phí AI tháng này"
        description="Tính từ số token Google thật sự tính phí. Lượt dùng key miễn phí ghi 0 USD."
      />
      <CardBody className="space-y-4 pt-3">
        <div className="flex flex-wrap items-end justify-between gap-2">
          <p className="text-2xl font-semibold tabular-nums text-fg">
            {usd(b.spentUsd)} <span className="text-sm font-normal text-fg-muted">/ {b.monthlyUsd} USD</span>
          </p>
          <p className="text-[13px] text-fg-muted">
            Ước cả tháng ~{usd(data.projectedUsd)}
            {data.videos > 0 && ` · ~${usd(data.costPerVideo)}/video`}
          </p>
        </div>
        <ProgressBar value={pct} tone={tone} />
        {b.overBudget && (
          <Alert tone="danger">
            Đã chạm trần ngân sách.{' '}
            {b.mode === 'pause' ? 'Tự đăng đang tạm dừng tới tháng sau.' : b.mode === 'edge' ? 'Video mới dùng giọng Edge miễn phí tới tháng sau.' : 'Chế độ chỉ cảnh báo: app vẫn chạy bình thường.'}
          </Alert>
        )}
        {data.byKind.length > 0 && (
          <ul className="grid gap-x-6 gap-y-1.5 text-[13px] sm:grid-cols-2">
            {data.byKind.map((k) => (
              <li key={k.kind} className="flex justify-between gap-3">
                <span className="text-fg-muted">
                  {KIND[k.kind] ?? k.kind} · {k.calls} lượt{k.freeCalls > 0 ? ` (${k.freeCalls} miễn phí)` : ''}
                </span>
                <span className="tabular-nums text-fg">{usd(k.costUsd)}</span>
              </li>
            ))}
          </ul>
        )}
        <div className="grid gap-3 border-t border-line pt-4 sm:grid-cols-[140px_minmax(0,1fr)_auto] sm:items-end">
          <Field label="Trần mỗi tháng (USD)">
            <Input type="number" min={0} step={1} value={budget} onChange={(e) => setBudget(e.target.value)} />
          </Field>
          <Field label="Khi chạm trần">
            <Select value={mode} onChange={(e) => setMode(e.target.value)}>
              {MODES.map((m) => (
                <option key={m.value} value={m.value}>
                  {m.label}
                </option>
              ))}
            </Select>
          </Field>
          <Button variant="primary" icon={<Save className="h-4 w-4" />} loading={saving} disabled={!dirty} onClick={save}>
            Lưu
          </Button>
        </div>
      </CardBody>
    </Card>
  );
}
