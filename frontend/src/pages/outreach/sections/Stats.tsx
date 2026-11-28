import { useMemo, useState } from 'react';
import { Link2, Table2, BarChart3 } from 'lucide-react';
import { Button, Card, CardHeader, Field, Input, Segmented, Skeleton } from '../../../components/ui';
import { Breakdown, outreach, Stats } from '../outreachApi';
import { CopyButton, rate, Stat, useLoad } from '../shared';

// Validated pair (dataviz validator, light + dark): views = accent token, clicks = rose 600 / 500.
const VIEW = 'bg-accent';
const CLICK = 'bg-rose-600 dark:bg-rose-500';

function DayChart({ s }: { s: Stats }) {
  const [hover, setHover] = useState<number | null>(null);
  const [table, setTable] = useState(false);
  const max = Math.max(1, ...s.perDay.map((d) => Math.max(d.views, d.clicks)));
  const label = (iso: string) => {
    const [, m, d] = iso.split('-');
    return `${Number(d)}/${Number(m)}`;
  };
  const step = s.perDay.length > 14 ? 3 : s.perDay.length > 7 ? 2 : 1;

  return (
    <Card>
      <CardHeader
        title="Theo ngày"
        description="Lượt xem trang /cheo-uy-tinh và lượt bấm “Mở Facebook” (giờ Việt Nam)."
        actions={
          <div className="flex items-center gap-3">
            <span className="flex items-center gap-1.5 text-xs text-fg-muted"><span className={`h-2.5 w-2.5 rounded-sm ${VIEW}`} />Lượt xem</span>
            <span className="flex items-center gap-1.5 text-xs text-fg-muted"><span className={`h-2.5 w-2.5 rounded-sm ${CLICK}`} />Lượt bấm</span>
            <Button size="sm" variant="ghost" icon={table ? <BarChart3 className="h-3.5 w-3.5" /> : <Table2 className="h-3.5 w-3.5" />} onClick={() => setTable(!table)}>
              {table ? 'Biểu đồ' : 'Bảng'}
            </Button>
          </div>
        }
      />
      <div className="p-4 pt-0">
        {table ? (
          <div className="max-h-72 overflow-y-auto">
            <table className="w-full text-sm">
              <thead className="text-left text-xs text-fg-muted">
                <tr><th className="py-1.5 font-medium">Ngày</th><th className="text-right font-medium">Lượt xem</th><th className="text-right font-medium">Lượt bấm</th><th className="text-right font-medium">Tỉ lệ</th></tr>
              </thead>
              <tbody className="divide-y divide-line tabular-nums text-fg">
                {[...s.perDay].reverse().map((d) => (
                  <tr key={d.date}><td className="py-1.5">{label(d.date)}</td><td className="text-right">{d.views}</td><td className="text-right">{d.clicks}</td><td className="text-right text-fg-muted">{rate(d.clicks, d.views)}</td></tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="relative">
            <div className="pointer-events-none absolute inset-x-0 top-0 flex h-48 flex-col justify-between">
              {[max, Math.round(max / 2), 0].map((v, i) => (
                <div key={i} className="flex items-center gap-2">
                  <span className="w-6 text-right text-[10px] tabular-nums text-fg-subtle">{v}</span>
                  <div className="h-px flex-1 bg-line/70" />
                </div>
              ))}
            </div>
            <div className="ml-8 flex h-48 items-end gap-1">
              {s.perDay.map((d, i) => (
                <div key={d.date} className="relative flex h-full flex-1 items-end justify-center gap-[2px]"
                  onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)}>
                  {hover === i && <div className="absolute inset-y-0 -inset-x-0.5 rounded bg-fg/[0.04]" />}
                  <div className={`relative w-full max-w-[14px] rounded-t ${VIEW}`} style={{ height: `${(100 * d.views) / max}%`, minHeight: d.views ? 2 : 0 }} />
                  <div className={`relative w-full max-w-[14px] rounded-t ${CLICK}`} style={{ height: `${(100 * d.clicks) / max}%`, minHeight: d.clicks ? 2 : 0 }} />
                  {hover === i && (
                    <div className={`absolute bottom-full z-10 mb-1 whitespace-nowrap rounded-md border border-line bg-surface px-2.5 py-1.5 text-xs shadow-lg ${i > s.perDay.length / 2 ? 'right-0' : 'left-0'}`}>
                      <p className="font-medium text-fg">Ngày {label(d.date)}</p>
                      <p className="flex items-center gap-1.5 text-fg-muted"><span className={`h-2 w-2 rounded-sm ${VIEW}`} />{d.views} lượt xem</p>
                      <p className="flex items-center gap-1.5 text-fg-muted"><span className={`h-2 w-2 rounded-sm ${CLICK}`} />{d.clicks} lượt bấm · {rate(d.clicks, d.views)}</p>
                    </div>
                  )}
                </div>
              ))}
            </div>
            <div className="ml-8 mt-1 flex gap-1">
              {s.perDay.map((d, i) => (
                <span key={d.date} className="flex-1 text-center text-[10px] tabular-nums text-fg-subtle">{i % step === 0 || i === s.perDay.length - 1 ? label(d.date) : ''}</span>
              ))}
            </div>
          </div>
        )}
      </div>
    </Card>
  );
}

function BreakdownCard({ title, description, rows, empty }: { title: string; description?: string; rows: Breakdown[]; empty: string }) {
  const max = Math.max(1, ...rows.map((r) => r.views));
  return (
    <Card>
      <CardHeader title={title} description={description} />
      <div className="space-y-2.5 p-4 pt-0">
        {rows.length === 0 && <p className="text-sm text-fg-muted">{empty}</p>}
        {rows.map((r) => (
          <div key={r.key}>
            <div className="flex items-baseline justify-between gap-2 text-sm">
              <span className="min-w-0 truncate text-fg" title={r.key}>{r.label ?? r.key}</span>
              <span className="shrink-0 tabular-nums text-xs text-fg-muted">{r.views} xem · {r.clicks} bấm · {rate(r.clicks, r.views)}</span>
            </div>
            <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-muted">
              <div className={`h-full rounded-full ${VIEW}`} style={{ width: `${(100 * r.views) / max}%` }} />
            </div>
          </div>
        ))}
      </div>
    </Card>
  );
}

function UtmBuilder() {
  const [campaign, setCampaign] = useState('campaign_01');
  const [content, setContent] = useState('');
  const [source, setSource] = useState('facebook');
  const link = useMemo(() => {
    const q = new URLSearchParams({ utm_source: source || 'facebook', utm_medium: 'group', utm_campaign: campaign || 'campaign_01' });
    if (content) q.set('utm_content', content);
    return `https://comment-follow-cheo-uy-tinh.vercel.app/cheo-uy-tinh?${q.toString()}`;
  }, [campaign, content, source]);
  return (
    <Card>
      <CardHeader title={<span className="flex items-center gap-2"><Link2 className="h-4 w-4" /> Tạo link UTM nhanh</span>}
        description="Link theo dõi riêng cho từng Group / Page phụ có sẵn trong Quản lý chiến dịch; ở đây tạo link tuỳ ý." />
      <div className="grid gap-3 p-4 pt-0 sm:grid-cols-3">
        <Field label="utm_source"><Input value={source} onChange={(e) => setSource(e.target.value)} /></Field>
        <Field label="utm_campaign"><Input value={campaign} onChange={(e) => setCampaign(e.target.value)} /></Field>
        <Field label="utm_content" optional><Input value={content} onChange={(e) => setContent(e.target.value)} placeholder="page_a_group_01" /></Field>
        <div className="flex items-center gap-2 sm:col-span-3">
          <code className="min-w-0 flex-1 truncate rounded-md bg-subtle px-2 py-1.5 text-xs text-fg-muted">{link}</code>
          <CopyButton text={link} />
        </div>
      </div>
    </Card>
  );
}

export function StatsSection() {
  const [days, setDays] = useState(14);
  const { data: s, loading } = useLoad(() => outreach.stats(days), [days]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Segmented value={days} onChange={setDays} options={[{ value: 7, label: '7 ngày' }, { value: 14, label: '14 ngày' }, { value: 30, label: '30 ngày' }, { value: 90, label: '90 ngày' }]} />
        <p className="text-xs text-fg-subtle">Lượt bấm = lượt mở Facebook, không phải lượt Follow đã xác nhận.</p>
      </div>
      {loading && !s ? <Skeleton className="h-72 w-full" /> : s && (
        <>
          <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
            <Stat label="Tổng lượt truy cập" value={s.views} hint={`${s.uniqueVisitors} người (trình duyệt) khác nhau`} accent />
            <Stat label="Bấm sang Facebook" value={s.clicks} hint={`Tỉ lệ ${rate(s.clicks, s.views)}`} accent />
            <Stat label="Điện thoại" value={s.mobileViews} hint={rate(s.mobileViews, s.views) + ' lượt xem'} />
            <Stat label="Máy tính" value={s.desktopViews} hint={rate(s.desktopViews, s.views) + ' lượt xem'} />
          </div>
          <DayChart s={s} />
          <div className="grid gap-4 lg:grid-cols-3">
            <BreakdownCard title="Theo chiến dịch" description="utm_campaign" rows={s.campaigns} empty="Chưa có lượt truy cập từ link chiến dịch." />
            <BreakdownCard title="Theo Group × Page phụ" description="utm_content" rows={s.contents} empty="Chưa có lượt truy cập từ link Group." />
            <BreakdownCard title="Theo nguồn" description="utm_source hoặc trang giới thiệu" rows={s.sources} empty="Chưa có dữ liệu." />
          </div>
        </>
      )}
      <UtmBuilder />
    </div>
  );
}
