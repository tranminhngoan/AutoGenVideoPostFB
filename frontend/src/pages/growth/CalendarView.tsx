import { useMemo, useState } from 'react';
import { ChevronLeft, ChevronRight, Plus } from 'lucide-react';
import { Button, IconButton, Segmented, Select, cn } from '../../components/ui';
import { useToast } from '../../components/feedback';
import { navigate } from '../../lib/router';
import { growth, type Content, type ContentKind } from '../../services/growth';
import { addDays, dm, KIND_LABEL, monthGrid, STATUS_LABEL, vnDate, vnTime, vnToUtc, vnToday, weekDays, WEEKDAY_SHORT, dayOfWeek } from '../../lib/growthMath';
import { ContentEditor, type ContentDefaults } from './ContentEditor';
import { ContentBadge, KindIcon, LoadError, LoadingBlock, NoCampaign, contentTitle, useLoad, type GrowthCtx } from './shared';

const CHIP: Record<string, string> = {
  Draft: 'border-line bg-subtle text-fg-muted',
  PendingApproval: 'border-warning/40 bg-warning/[0.08] text-fg',
  Scheduled: 'border-accent/30 bg-accent/[0.07] text-fg',
  Publishing: 'border-accent/50 bg-accent/[0.12] text-fg',
  Published: 'border-success/30 bg-success/[0.07] text-fg',
  Failed: 'border-danger/40 bg-danger/[0.07] text-fg',
  Cancelled: 'border-line bg-transparent text-fg-subtle line-through',
};

export function CalendarView({ ctx }: { ctx: GrowthCtx }) {
  const campaign = ctx.campaign;
  const toast = useToast();
  const [mode, setMode] = useState<'week' | 'month'>('week');
  const [anchor, setAnchor] = useState(() => {
    const t = vnToday();
    return campaign && (t < campaign.startDate || t > campaign.endDate) ? campaign.startDate : t;
  });
  const [kind, setKind] = useState<'' | ContentKind>('');
  const [showCancelled, setShowCancelled] = useState(false);
  const [editor, setEditor] = useState<{ item?: Content; defaults?: ContentDefaults } | null>(null);
  const [dragId, setDragId] = useState<string | null>(null);

  const days = mode === 'week' ? weekDays(anchor) : monthGrid(anchor);
  const from = days[0];
  const to = addDays(days[days.length - 1], 1);
  const { data, error, loading, reload, setData } = useLoad(
    () => (campaign ? growth.contents(campaign.id, { from: vnToUtc(from, '00:00'), to: vnToUtc(to, '00:00'), kind: kind || undefined }) : Promise.resolve([])),
    [campaign?.id, from, to, kind],
    ctx.onNeedKey
  );

  const byDay = useMemo(() => {
    const map = new Map<string, Content[]>();
    for (const c of data ?? []) {
      if (!showCancelled && c.status === 'Cancelled') continue;
      const at = c.publishedAtUtc ?? c.scheduledAtUtc;
      if (!at) continue;
      const d = vnDate(at);
      map.set(d, [...(map.get(d) ?? []), c]);
    }
    for (const list of map.values()) list.sort((a, b) => (a.scheduledAtUtc ?? '').localeCompare(b.scheduledAtUtc ?? ''));
    return map;
  }, [data, showCancelled]);
  const unscheduled = (data ?? []).filter((c) => !c.scheduledAtUtc && c.status !== 'Cancelled');

  if (!campaign) return <NoCampaign ctx={ctx} action={ctx.page && <Button variant="primary" onClick={() => navigate('/growth/campaigns')}>Tạo chiến dịch</Button>} />;

  const inCampaign = (d: string) => d >= campaign.startDate && d <= campaign.endDate;
  const closed = campaign.status === 'Completed' || campaign.status === 'Cancelled';
  const today = vnToday();

  /** Drag-and-drop: same time of day, another date. Only items not yet posted move. */
  const drop = async (day: string) => {
    const item = data?.find((c) => c.id === dragId);
    setDragId(null);
    if (!item || !item.scheduledAtUtc) return;
    if (!['Draft', 'PendingApproval', 'Scheduled', 'Failed'].includes(item.status)) return toast.error('Nội dung này không dời được', STATUS_LABEL[item.status]);
    if (!inCampaign(day)) return toast.error('Ngoài thời gian chiến dịch');
    const at = vnToUtc(day, vnTime(item.scheduledAtUtc));
    if (vnDate(item.scheduledAtUtc) === day) return;
    const before = data;
    setData((list) => (list ?? []).map((c) => (c.id === item.id ? { ...c, scheduledAtUtc: at } : c)));
    try {
      const saved = await growth.updateContent(campaign.id, item.id, { scheduledAtUtc: at });
      setData((list) => (list ?? []).map((c) => (c.id === saved.id ? saved : c)));
      toast.success(`Đã dời sang ${dm(day)}`);
    } catch (e) {
      setData(before);
      toast.error('Không dời được', (e as Error).message);
    }
  };

  const shift = (n: number) => setAnchor((a) => (mode === 'week' ? addDays(a, 7 * n) : (() => {
    const [y, m] = a.split('-').map(Number);
    const t = new Date(Date.UTC(y, m - 1 + n, 1));
    return `${t.getUTCFullYear()}-${String(t.getUTCMonth() + 1).padStart(2, '0')}-01`;
  })()));

  const title = mode === 'week' ? `${dm(days[0])} – ${dm(days[6])}` : `Tháng ${Number(anchor.slice(5, 7))}/${anchor.slice(0, 4)}`;

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-2">
          <IconButton label="Trước" onClick={() => shift(-1)}><ChevronLeft className="h-4 w-4" /></IconButton>
          <div className="min-w-[130px] text-center text-[15px] font-semibold tabular-nums text-fg">{title}</div>
          <IconButton label="Sau" onClick={() => shift(1)}><ChevronRight className="h-4 w-4" /></IconButton>
          <Button size="sm" variant="ghost" onClick={() => setAnchor(inCampaign(today) ? today : campaign.startDate)}>Hôm nay</Button>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Select size="sm" aria-label="Lọc loại nội dung" value={kind} onChange={(e) => setKind(e.target.value as ContentKind | '')} className="w-36">
            <option value="">Mọi loại</option>
            {Object.entries(KIND_LABEL).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </Select>
          <label className="flex items-center gap-1.5 text-xs text-fg-muted">
            <input type="checkbox" checked={showCancelled} onChange={(e) => setShowCancelled(e.target.checked)} /> Hiện đã huỷ
          </label>
          <Segmented size="sm" options={[{ value: 'week', label: 'Tuần' }, { value: 'month', label: 'Tháng' }]} value={mode} onChange={setMode} />
          {!closed && (
            <Button size="sm" variant="primary" icon={<Plus className="h-3.5 w-3.5" />} onClick={() => setEditor({ defaults: { date: inCampaign(today) ? today : campaign.startDate } })}>
              Thêm
            </Button>
          )}
        </div>
      </div>

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data ? (
        <LoadingBlock rows={2} />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-line bg-surface shadow-card">
          <div className={cn('grid min-w-[720px] grid-cols-7')}>
            {(mode === 'week' ? days : days.slice(0, 7)).map((d) => (
              <div key={'h' + d} className="border-b border-line px-2 py-1.5 text-center text-[11px] font-medium uppercase tracking-wide text-fg-subtle">
                {WEEKDAY_SHORT[dayOfWeek(d)]}
              </div>
            ))}
            {days.map((d) => {
              const items = byDay.get(d) ?? [];
              const outside = mode === 'month' && d.slice(0, 7) !== anchor.slice(0, 7);
              return (
                <div
                  key={d}
                  onDragOver={(e) => { if (dragId && inCampaign(d)) e.preventDefault(); }}
                  onDrop={() => drop(d)}
                  className={cn(
                    'group relative border-b border-r border-line p-1.5 [&:nth-child(7n)]:border-r-0',
                    mode === 'week' ? 'min-h-[260px]' : 'min-h-[112px]',
                    !inCampaign(d) && 'bg-subtle/70',
                    outside && 'opacity-50'
                  )}
                >
                  <div className="mb-1 flex items-center justify-between">
                    <span className={cn('flex h-6 min-w-6 items-center justify-center rounded px-1 text-xs tabular-nums', d === today ? 'bg-fg font-semibold text-canvas' : 'text-fg-muted')}>
                      {dm(d)}
                    </span>
                    {inCampaign(d) && !closed && (
                      <button type="button" aria-label={`Thêm nội dung ngày ${dm(d)}`} onClick={() => setEditor({ defaults: { date: d } })} className="rounded p-0.5 text-fg-subtle opacity-0 hover:bg-muted hover:text-fg focus:opacity-100 group-hover:opacity-100">
                        <Plus className="h-3.5 w-3.5" />
                      </button>
                    )}
                  </div>
                  <div className="space-y-1">
                    {items.slice(0, mode === 'month' ? 3 : 20).map((c) => (
                      <button
                        key={c.id}
                        type="button"
                        draggable={['Draft', 'PendingApproval', 'Scheduled', 'Failed'].includes(c.status)}
                        onDragStart={() => setDragId(c.id)}
                        onDragEnd={() => setDragId(null)}
                        onClick={() => setEditor({ item: c })}
                        title={`${KIND_LABEL[c.kind]} · ${STATUS_LABEL[c.status]} · ${contentTitle(c)}`}
                        className={cn('flex w-full items-center gap-1 rounded border px-1.5 py-1 text-left text-[11px] leading-tight transition-colors hover:brightness-95', CHIP[c.status])}
                      >
                        <KindIcon kind={c.kind} className="h-3 w-3 shrink-0 opacity-70" />
                        <span className="shrink-0 tabular-nums text-fg-muted">{c.scheduledAtUtc ? vnTime(c.publishedAtUtc ?? c.scheduledAtUtc) : ''}</span>
                        <span className="min-w-0 flex-1 truncate">{contentTitle(c)}</span>
                      </button>
                    ))}
                    {mode === 'month' && items.length > 3 && (
                      <button type="button" className="text-[11px] text-accent hover:underline" onClick={() => { setMode('week'); setAnchor(d); }}>+{items.length - 3} nữa</button>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      <div className="flex flex-wrap gap-3 text-[11px] text-fg-muted">
        {Object.entries(STATUS_LABEL).map(([k, v]) => (
          <span key={k} className="flex items-center gap-1.5"><span className={cn('h-3 w-3 rounded-sm border', CHIP[k])} />{v}</span>
        ))}
        <span className="text-fg-subtle">· Kéo thả để dời ngày (giữ nguyên giờ). Vùng xám: ngoài thời gian chiến dịch.</span>
      </div>

      {unscheduled.length > 0 && (
        <div className="rounded-lg border border-line bg-surface p-4 shadow-card">
          <div className="mb-2 text-[13px] font-semibold text-fg">Bản nháp chưa có giờ</div>
          <div className="space-y-1">
            {unscheduled.map((c) => (
              <button key={c.id} type="button" onClick={() => setEditor({ item: c })} className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-[13px] hover:bg-muted">
                <KindIcon kind={c.kind} />
                <span className="min-w-0 flex-1 truncate">{contentTitle(c)}</span>
                <ContentBadge status={c.status} />
              </button>
            ))}
          </div>
        </div>
      )}

      {editor && (
        <ContentEditor
          ctx={ctx}
          campaign={campaign}
          item={editor.item}
          defaults={editor.defaults}
          open
          onClose={() => setEditor(null)}
          onSaved={() => { setEditor(null); reload(); ctx.reloadCampaigns(); }}
        />
      )}
    </div>
  );
}
