import { useState } from 'react';
import { AlertTriangle, CalendarClock, ExternalLink, FileDown, Hand, Pause, Play, RefreshCw, Sparkles, Square, Lightbulb, Info } from 'lucide-react';
import { Alert, Badge, Button, ButtonLink, Menu, ProgressBar, cn } from '../../components/ui';
import { useConfirm, useToast } from '../../components/feedback';
import { navigate } from '../../lib/router';
import { growth, type Content, type Kpi, type Overview, type Suggestions } from '../../services/growth';
import { dm, KIND_LABEL, num, vnShort, vnTime } from '../../lib/growthMath';
import { BarList, LineChart, PairBars } from './charts';
import { CampaignBadge, ContentBadge, KindIcon, LoadError, LoadingBlock, NoCampaign, PageAvatar, Section, contentTitle, useLoad, type GrowthCtx } from './shared';
import { ContentEditor } from './ContentEditor';

const HEADLINE = ['new_followers', 'views', 'engagement', 'growth_rate'];

const notToday = (o: Overview) => o.upcoming.filter((u) => !o.today.some((t) => t.id === u.id));

export function OverviewView({ ctx }: { ctx: GrowthCtx }) {
  const campaign = ctx.campaign;
  const { data, error, loading, reload } = useLoad(() => (campaign ? growth.overview(campaign.id) : Promise.resolve(null)), [campaign?.id], ctx.onNeedKey);
  const [editing, setEditing] = useState<Content | null>(null);

  if (!campaign) return <NoCampaign ctx={ctx} action={ctx.page && <Button variant="primary" onClick={() => navigate('/growth/campaigns')}>Tạo chiến dịch</Button>} />;
  if (loading && !data) return <LoadingBlock rows={4} />;
  if (error && !data) return <LoadError message={error} onRetry={reload} />;
  if (!data) return null;

  return (
    <div className="space-y-5">
      <CampaignHeader ctx={ctx} o={data} reload={reload} />
      <KpiGrid kpis={data.kpis} />
      <Charts o={data} />
      <div className="grid gap-5 lg:grid-cols-2">
        <TodayPanel o={data} onOpen={setEditing} />
        <AttentionPanel o={data} onOpen={setEditing} />
      </div>
      <SuggestionsPanel ctx={ctx} o={data} />
      {editing && (
        <ContentEditor ctx={ctx} campaign={data.campaign} item={editing} open onClose={() => setEditing(null)} onSaved={() => { setEditing(null); reload(); }} />
      )}
    </div>
  );
}

function CampaignHeader({ ctx, o, reload }: { ctx: GrowthCtx; o: Overview; reload: () => void }) {
  const c = o.campaign;
  const toast = useToast();
  const confirm = useConfirm();
  const [busy, setBusy] = useState<string | null>(null);

  const act = async (action: 'start' | 'pause' | 'resume' | 'complete' | 'cancel') => {
    const ask: Record<string, [string, string, boolean] | undefined> = {
      complete: ['Kết thúc chiến dịch?', 'Nội dung chưa đăng sẽ bị huỷ; follower cuối kỳ được ghi lại để làm báo cáo.', false],
      cancel: ['Huỷ chiến dịch?', 'Mọi nội dung chưa đăng sẽ bị huỷ.', true],
      start: ['Bắt đầu chiến dịch?', 'Follower hiện tại được đọc từ Meta làm mốc đầu kỳ. Nội dung đã lên lịch sẽ tự đăng đúng giờ.', false],
    };
    const q = ask[action];
    if (q && !(await confirm({ title: q[0], description: q[1], danger: q[2], confirmLabel: 'Đồng ý' }))) return;
    setBusy(action);
    try {
      const updated = await growth.action(c.id, action);
      ctx.setCampaign(updated);
      toast.success({ start: 'Chiến dịch đã bắt đầu', pause: 'Đã tạm dừng — không bài nào được đăng', resume: 'Đã tiếp tục', complete: 'Đã kết thúc chiến dịch', cancel: 'Đã huỷ chiến dịch' }[action]);
      reload();
    } catch (e) {
      toast.error('Không thực hiện được', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const sync = async () => {
    setBusy('sync');
    try {
      const r = await growth.sync(c.id);
      toast.success('Đã đồng bộ số liệu từ Meta', `${r.metricsAvailable} chỉ số có dữ liệu · ${r.metricsUnavailable} không khả dụng · ${r.postsRead} bài`);
      reload();
    } catch (e) {
      toast.error('Không đồng bộ được', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const exportReport = async (f: 'csv' | 'xls') => {
    try {
      await growth.downloadReport(c.id, f);
    } catch (e) {
      toast.error('Không xuất được báo cáo', (e as Error).message);
    }
  };

  return (
    <div className="overflow-hidden rounded-lg border border-line bg-surface shadow-card">
      <div className="flex flex-col gap-4 p-4 sm:flex-row sm:items-center sm:p-5">
        <div className="flex min-w-0 flex-1 items-center gap-3">
          <PageAvatar page={o.page} size={48} />
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <span className="truncate text-[15px] font-semibold text-fg">{o.page?.pageName ?? c.pageName}</span>
              {o.page && (o.page.isActive && o.page.hasToken ? <Badge tone="success" dot>Đã kết nối</Badge> : <Badge tone="danger" dot>Mất kết nối</Badge>)}
            </div>
            <div className="mt-0.5 flex flex-wrap gap-x-3 text-xs text-fg-subtle">
              {o.page && <span>ID {o.page.pageId}</span>}
              <span>{dm(c.startDate)} – {dm(c.endDate)}</span>
              {c.lastSyncedAtUtc && <span>Đồng bộ {vnShort(c.lastSyncedAtUtc)}</span>}
            </div>
          </div>
        </div>
        <div className="grid grid-cols-2 gap-6 sm:flex sm:gap-8">
          <div>
            <div className="text-xs text-fg-subtle">Follower hiện tại</div>
            <div className="text-xl font-semibold tabular-nums text-fg">{num(o.currentFollowers)}</div>
          </div>
          <div>
            <div className="text-xs text-fg-subtle">Đầu chiến dịch</div>
            <div className="text-xl font-semibold tabular-nums text-fg-muted">{num(c.startingFollowers)}</div>
          </div>
        </div>
      </div>
      <div className="flex flex-col gap-3 border-t border-line bg-subtle px-4 py-3 sm:flex-row sm:items-center sm:px-5">
        <div className="flex min-w-0 flex-1 items-center gap-3">
          <CampaignBadge status={c.status} />
          <div className="min-w-0 flex-1">
            <div className="flex justify-between text-xs text-fg-muted">
              <span>{c.status === 'Draft' ? 'Chưa bắt đầu' : `Ngày ${Math.min(o.progress.day, o.progress.totalDays)}/${o.progress.totalDays}`}</span>
              <span className="tabular-nums">{o.progress.published}/{o.progress.planned} bài theo kế hoạch</span>
            </div>
            <ProgressBar className="mt-1" value={o.progress.timePercent} tone="info" />
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          {c.status === 'Draft' && <Button size="sm" variant="primary" icon={<Play className="h-3.5 w-3.5" />} loading={busy === 'start'} onClick={() => act('start')}>Bắt đầu</Button>}
          {c.status === 'Running' && <Button size="sm" icon={<Pause className="h-3.5 w-3.5" />} loading={busy === 'pause'} onClick={() => act('pause')}>Tạm dừng</Button>}
          {c.status === 'Paused' && <Button size="sm" variant="primary" icon={<Play className="h-3.5 w-3.5" />} loading={busy === 'resume'} onClick={() => act('resume')}>Tiếp tục</Button>}
          {(c.status === 'Running' || c.status === 'Paused') && <Button size="sm" icon={<Square className="h-3.5 w-3.5" />} loading={busy === 'complete'} onClick={() => act('complete')}>Kết thúc</Button>}
          <Button size="sm" variant="ghost" icon={<RefreshCw className="h-3.5 w-3.5" />} loading={busy === 'sync'} onClick={sync}>Đồng bộ số liệu</Button>
          <Menu
            trigger={<Button size="sm" variant="ghost" icon={<FileDown className="h-3.5 w-3.5" />}>Xuất báo cáo</Button>}
            items={[
              { label: 'Xem báo cáo đầy đủ', onSelect: () => navigate('/growth/analytics?tab=report') },
              { label: 'Tải CSV', onSelect: () => exportReport('csv') },
              { label: 'Tải Excel (.xls)', onSelect: () => exportReport('xls') },
              ...(c.status === 'Draft' || c.status === 'Running' || c.status === 'Paused' ? [{ label: 'Huỷ chiến dịch', danger: true, divider: true, onSelect: () => act('cancel') }] : []),
            ]}
          />
        </div>
      </div>
      {!c.pageConnected && c.status !== 'Completed' && c.status !== 'Cancelled' && (
        <div className="border-t border-line px-4 py-3 sm:px-5">
          <Alert tone="danger" icon={<AlertTriangle className="h-4 w-4" />} title="Page chưa kết nối" action={<ButtonLink href="#/settings/facebook" size="sm" variant="secondary">Kết nối lại</ButtonLink>}>
            Lịch đăng sẽ thất bại cho tới khi Page được kết nối lại (dùng chức năng kết nối Page sẵn có).
          </Alert>
        </div>
      )}
    </div>
  );
}

function KpiGrid({ kpis }: { kpis: Kpi[] }) {
  const head = HEADLINE.map((k) => kpis.find((x) => x.key === k)).filter(Boolean) as Kpi[];
  const rest = kpis.filter((k) => !HEADLINE.includes(k.key));
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {head.map((k) => (
          <div key={k.key} className="rounded-lg border border-line bg-surface p-4 shadow-card">
            <div className="flex items-center justify-between gap-2 text-xs text-fg-muted">
              {k.label}
              {k.note && <span title={k.note}><Info className="h-3.5 w-3.5 text-fg-subtle" /></span>}
            </div>
            <div className={cn('mt-1.5 text-2xl font-semibold tabular-nums tracking-tight', k.value == null ? 'text-fg-subtle' : 'text-fg')}>
              {k.value == null ? 'Chưa có dữ liệu' : num(k.value, k.unit)}
            </div>
            {k.value == null && k.note && <p className="mt-1 line-clamp-2 text-[11px] text-fg-subtle">{k.note}</p>}
          </div>
        ))}
      </div>
      <div className="grid grid-cols-2 gap-x-6 gap-y-2 rounded-lg border border-line bg-surface px-4 py-3 shadow-card sm:grid-cols-3 lg:grid-cols-5">
        {rest.map((k) => (
          <div key={k.key} className="min-w-0" title={k.note ?? undefined}>
            <div className="truncate text-[11px] text-fg-subtle">{k.label}</div>
            <div className={cn('text-[15px] font-semibold tabular-nums', k.value == null ? 'text-fg-subtle' : 'text-fg')}>
              {k.value == null ? <span className="text-[13px] font-normal">Chưa có dữ liệu</span> : num(k.value, k.unit)}
              {k.source === 'manual' && k.value != null && <span className="ml-1 text-[10px] font-normal text-fg-subtle">nhập tay</span>}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function Charts({ o }: { o: Overview }) {
  const start = o.campaign.startDate;
  const pts = (pick: (d: Overview['daily'][number]) => number | null | undefined) => o.daily.map((d) => ({ label: dm(d.date), value: pick(d), muted: d.date < start }));
  const views = o.daily.some((d) => d.mediaViews != null) ? pts((d) => d.mediaViews) : pts((d) => d.contentViews);
  const engagement = o.daily.some((d) => d.engagements != null) ? pts((d) => d.engagements) : pts((d) => d.contentEngagement);
  const unavailable = (metric: string) => o.campaign.metricAvailability.find((m) => m.metric === metric && !m.ok)?.error;
  const why = (metric: string) => unavailable(metric) ? `Meta không trả về ${metric} cho Page này.` : o.campaign.lastSyncedAtUtc ? 'Meta chưa có số liệu cho các ngày này.' : 'Chưa đồng bộ số liệu — bấm "Đồng bộ số liệu".';
  const followers = o.daily.some((d) => d.newFollows != null) ? pts((d) => d.newFollows) : null;
  return (
    <div className="grid gap-5 lg:grid-cols-2">
      <Section title={followers ? 'Follower mới theo ngày' : 'Số follower theo ngày'} description="Vùng xám: 7 ngày trước chiến dịch (đối chứng).">
        <LineChart ariaLabel="Follower theo ngày" points={followers ?? pts((d) => d.followers)} empty={why('page_daily_follows_unique')} />
      </Section>
      <Section title="Lượt xem theo ngày" description={o.daily.some((d) => d.mediaViews != null) ? 'Lượt xem nội dung của cả Page (page_media_view).' : 'Lượt xem tăng thêm của các bài trong chiến dịch.'}>
        <LineChart ariaLabel="Lượt xem theo ngày" points={views} empty={why('page_media_view')} />
      </Section>
      <Section title="Tiếp cận theo ngày" description="Người xem duy nhất của Page (page_total_media_view_unique).">
        <LineChart ariaLabel="Tiếp cận theo ngày" points={pts((d) => d.mediaViewers)} empty={why('page_total_media_view_unique')} />
      </Section>
      <Section title="Tương tác theo ngày">
        <LineChart ariaLabel="Tương tác theo ngày" points={engagement} empty={why('page_post_engagements')} />
      </Section>
      <Section title="Hiệu quả theo loại nội dung" description="Tương tác trung bình mỗi bài (cảm xúc + bình luận + chia sẻ).">
        <BarList
          items={o.byKind.filter((k) => k.kind !== 'Story').map((k) => ({ label: KIND_LABEL[k.kind], value: k.avgEngagement, hint: `${k.published} bài, ${k.withMetrics} có số liệu` }))}
          empty={o.byKind.length === 0 ? 'Chưa có bài nào được đăng.' : 'Chưa đọc được số liệu bài đăng.'}
        />
        {o.byKind.some((k) => k.kind === 'Story') && <p className="mt-3 text-xs text-fg-subtle">Story: {o.byKind.find((k) => k.kind === 'Story')!.published} đã đăng — Meta không cung cấp số liệu Story của Page qua API.</p>}
      </Section>
      <Section title="Trước và trong chiến dịch" description="So sánh 7 ngày trước với các ngày đã chạy (số liệu Meta).">
        <PairBars
          rows={[
            { label: 'Follow mới', before: o.before?.newFollows, during: o.during?.newFollows },
            { label: 'Lượt truy cập Page', before: o.before?.pageViews, during: o.during?.pageViews },
            { label: 'Lượt xem nội dung', before: o.before?.mediaViews, during: o.during?.mediaViews },
            { label: 'Tương tác', before: o.before?.engagements, during: o.during?.engagements },
          ]}
          empty="Chưa có số liệu Page theo ngày để so sánh (cần quyền read_insights)."
        />
      </Section>
    </div>
  );
}

function ContentRow({ c, onOpen }: { c: Content; onOpen: (c: Content) => void }) {
  return (
    <button type="button" onClick={() => onOpen(c)} className="flex w-full items-center gap-3 rounded-md px-2 py-2 text-left hover:bg-muted">
      <span className="w-11 shrink-0 text-xs tabular-nums text-fg-muted">{c.scheduledAtUtc ? vnTime(c.scheduledAtUtc) : '—'}</span>
      <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded bg-muted text-fg-muted"><KindIcon kind={c.kind} /></span>
      <span className="min-w-0 flex-1 truncate text-[13px] text-fg">{contentTitle(c)}</span>
      <ContentBadge status={c.status} />
    </button>
  );
}

function TodayPanel({ o, onOpen }: { o: Overview; onOpen: (c: Content) => void }) {
  return (
    <Section title="Lịch đăng hôm nay" actions={<Button size="sm" variant="ghost" icon={<CalendarClock className="h-3.5 w-3.5" />} onClick={() => navigate('/growth/calendar')}>Mở lịch</Button>}>
      {o.today.length === 0 ? (
        <p className="py-6 text-center text-[13px] text-fg-subtle">Hôm nay chưa có nội dung nào.</p>
      ) : (
        <div className="-mx-2">{o.today.map((c) => <ContentRow key={c.id} c={c} onOpen={onOpen} />)}</div>
      )}
      {notToday(o).length > 0 && (
        <>
          <div className="mb-1 mt-4 text-xs font-medium uppercase tracking-wide text-fg-subtle">Sắp đăng</div>
          <div className="-mx-2">
            {notToday(o).map((c) => (
              <button key={c.id} type="button" onClick={() => onOpen(c)} className="flex w-full items-center gap-3 rounded-md px-2 py-1.5 text-left hover:bg-muted">
                <span className="w-20 shrink-0 text-xs tabular-nums text-fg-muted">{vnShort(c.scheduledAtUtc)}</span>
                <KindIcon kind={c.kind} className="h-3.5 w-3.5 text-fg-subtle" />
                <span className="min-w-0 flex-1 truncate text-[13px] text-fg">{contentTitle(c)}</span>
                {c.status === 'PendingApproval' && <Badge tone="warning">Chờ duyệt</Badge>}
              </button>
            ))}
          </div>
        </>
      )}
    </Section>
  );
}

function AttentionPanel({ o, onOpen }: { o: Overview; onOpen: (c: Content) => void }) {
  return (
    <Section title="Cần chú ý">
      {o.failed.length === 0 && o.dueManual.length === 0 && o.insights.length === 0 ? (
        <p className="py-6 text-center text-[13px] text-fg-subtle">Không có vấn đề nào.</p>
      ) : (
        <div className="space-y-3">
          {o.failed.map((c) => (
            <button key={c.id} type="button" onClick={() => onOpen(c)} className="block w-full rounded-md border border-danger/25 bg-danger/[0.05] p-3 text-left hover:bg-danger/10">
              <div className="flex items-center gap-2 text-[13px] font-medium text-fg"><AlertTriangle className="h-3.5 w-3.5 text-danger" /> {contentTitle(c)}</div>
              <p className="mt-1 line-clamp-2 text-xs text-fg-muted">{c.lastError}</p>
            </button>
          ))}
          {o.dueManual.map((c) => (
            <button key={c.id} type="button" onClick={() => onOpen(c)} className="block w-full rounded-md border border-warning/30 bg-warning/[0.06] p-3 text-left">
              <div className="flex items-center gap-2 text-[13px] font-medium text-fg"><Hand className="h-3.5 w-3.5 text-warning" /> Đến giờ đăng thủ công: {contentTitle(c)}</div>
            </button>
          ))}
          {o.insights.map((t, i) => (
            <div key={i} className="flex gap-2 text-[13px] text-fg-muted"><Lightbulb className="mt-0.5 h-3.5 w-3.5 shrink-0 text-fg-subtle" /> {t}</div>
          ))}
        </div>
      )}
    </Section>
  );
}

function SuggestionsPanel({ ctx, o }: { ctx: GrowthCtx; o: Overview }) {
  const [data, setData] = useState<Suggestions | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const load = async (ai: boolean) => {
    setBusy(true);
    setError(null);
    try {
      setData(await growth.suggestions(o.campaign.id, ai));
      if (ai) ctx.reloadCampaigns();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Section
      title="Gợi ý cho ngày tiếp theo"
      description="Dựa trên số liệu thật của chiến dịch. AI chỉ đề xuất — không tự đổi lịch đã duyệt."
      actions={
        <>
          <Button size="sm" variant="ghost" loading={busy && !data?.fromAi} onClick={() => load(false)}>Gợi ý nhanh</Button>
          <Button size="sm" variant="secondary" icon={<Sparkles className="h-3.5 w-3.5" />} loading={busy} onClick={() => load(true)}>Phân tích bằng AI</Button>
        </>
      }
    >
      {error && <Alert tone="danger">{error}</Alert>}
      {!data && !error && <p className="text-[13px] text-fg-subtle">Bấm "Gợi ý nhanh" (miễn phí) hoặc "Phân tích bằng AI" (tốn ngân sách AI của chiến dịch).</p>}
      {data && (
        <>
          <ul className="space-y-2">
            {data.suggestions.map((s, i) => (
              <li key={i} className="flex gap-2 text-[13px] text-fg"><span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-accent" /> {s}</li>
            ))}
          </ul>
          <p className="mt-3 text-xs text-fg-subtle">Cơ sở: {data.basis}{data.fromAi ? ` · AI ${data.costUsd.toFixed(4)} USD` : ' · không dùng AI'}</p>
        </>
      )}
      {o.page && (
        <a className="mt-3 inline-flex items-center gap-1 text-xs text-accent hover:underline" href={`https://www.facebook.com/${o.page.pageId}`} target="_blank" rel="noreferrer noopener">
          Mở Page trên Facebook <ExternalLink className="h-3 w-3" />
        </a>
      )}
    </Section>
  );
}
