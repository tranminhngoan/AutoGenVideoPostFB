import { useState } from 'react';
import { CheckCircle2, Circle, ExternalLink, FileDown, Printer, Sparkles } from 'lucide-react';
import { Alert, Badge, Button, Field, Input, Tabs, Textarea, cn } from '../../components/ui';
import { useToast } from '../../components/feedback';
import { navigate, useRoute } from '../../lib/router';
import { growth, type PostRow, type Report } from '../../services/growth';
import { dm, KIND_LABEL, num, STYLE_LABEL, vnShort, vnToday } from '../../lib/growthMath';
import { BarList, HourColumns, LineChart } from './charts';
import { KindLabel, LoadError, LoadingBlock, NoCampaign, Section, useLoad, type GrowthCtx } from './shared';

const TABS = [
  { id: 'posts', label: 'Từng bài đăng' },
  { id: 'ranking', label: 'Xếp hạng' },
  { id: 'hours', label: 'Giờ đăng' },
  { id: 'followers', label: 'Follower & Meta One' },
  { id: 'report', label: 'Báo cáo' },
  { id: 'logs', label: 'Nhật ký' },
] as const;
type Tab = (typeof TABS)[number]['id'];

export function AnalyticsView({ ctx }: { ctx: GrowthCtx }) {
  const route = useRoute();
  const tab = (TABS.find((t) => t.id === route.query.get('tab'))?.id ?? 'posts') as Tab;
  if (!ctx.campaign) return <NoCampaign ctx={ctx} action={ctx.page && <Button variant="primary" onClick={() => navigate('/growth/campaigns')}>Tạo chiến dịch</Button>} />;
  return (
    <div className="space-y-4">
      <Tabs items={TABS.map((t) => ({ id: t.id, label: t.label }))} value={tab} onChange={(id) => navigate(`/growth/analytics?tab=${id}`)} />
      {tab === 'posts' && <PostsTab ctx={ctx} />}
      {tab === 'ranking' && <RankingTab ctx={ctx} />}
      {tab === 'hours' && <HoursTab ctx={ctx} />}
      {tab === 'followers' && <FollowersTab ctx={ctx} />}
      {tab === 'report' && <ReportTab ctx={ctx} />}
      {tab === 'logs' && <LogsTab ctx={ctx} />}
    </div>
  );
}

const cell = (v?: number | null) => <td className="px-3 py-2 text-right tabular-nums">{v == null ? <span className="text-fg-subtle">—</span> : num(v)}</td>;

function PostTable({ rows, compact }: { rows: PostRow[]; compact?: boolean }) {
  if (rows.length === 0) return <p className="py-6 text-center text-[13px] text-fg-subtle">Chưa có bài nào.</p>;
  return (
    <div className="-mx-4 overflow-x-auto sm:-mx-5">
      <table className="w-full min-w-[760px] text-[13px]">
        <thead>
          <tr className="border-b border-line text-left text-xs text-fg-subtle">
            <th className="px-3 py-2 font-medium sm:pl-5">Nội dung</th>
            <th className="px-3 py-2 font-medium">Đăng</th>
            <th className="px-3 py-2 text-right font-medium">Views</th>
            <th className="px-3 py-2 text-right font-medium">Reach</th>
            {!compact && <th className="px-3 py-2 text-right font-medium">Cảm xúc</th>}
            <th className="px-3 py-2 text-right font-medium">Bình luận</th>
            <th className="px-3 py-2 text-right font-medium">Chia sẻ</th>
            <th className="px-3 py-2 text-right font-medium">Tương tác</th>
            <th className="px-3 py-2 text-right font-medium sm:pr-5">Follow</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.contentId} className="border-b border-line last:border-0 hover:bg-subtle">
              <td className="max-w-[320px] px-3 py-2 sm:pl-5">
                <div className="flex items-center gap-2 text-xs"><KindLabel kind={r.kind} />{r.style && <Badge>{STYLE_LABEL[r.style] ?? r.style}</Badge>}</div>
                <div className="truncate text-fg" title={r.captionExcerpt}>{r.title || r.captionExcerpt || '—'}</div>
                {r.metricError && <div className="truncate text-[11px] text-fg-subtle" title={r.metricError}>{r.metricError}</div>}
              </td>
              <td className="whitespace-nowrap px-3 py-2 text-fg-muted">
                {r.localDate?.slice(0, 5)} {r.localTime}
                {r.permalinkUrl && <a href={r.permalinkUrl} target="_blank" rel="noreferrer noopener" className="ml-1 inline-flex text-accent" aria-label="Mở bài"><ExternalLink className="h-3 w-3" /></a>}
              </td>
              {cell(r.views)}
              {cell(r.reach)}
              {!compact && cell(r.reactions)}
              {cell(r.comments)}
              {cell(r.shares)}
              {cell(r.engagement)}
              <td className="px-3 py-2 text-right tabular-nums sm:pr-5">{r.follows == null ? <span className="text-fg-subtle">—</span> : num(r.follows)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PostsTab({ ctx }: { ctx: GrowthCtx }) {
  const { data, error, loading, reload } = useLoad(() => growth.posts(ctx.campaign!.id), [ctx.campaign!.id], ctx.onNeedKey);
  if (loading && !data) return <LoadingBlock rows={2} />;
  if (error) return <LoadError message={error} onRetry={reload} />;
  return (
    <Section title="Phân tích từng bài đăng" description="Số liệu trọn đời mỗi bài, đọc từ Meta mỗi giờ. '—' = Meta không cung cấp hoặc chưa đọc được (không phải 0). Follow = follower Meta ghi nhận cho Reels (nếu Page được cấp số liệu này).">
      <PostTable rows={data ?? []} />
    </Section>
  );
}

function RankingTab({ ctx }: { ctx: GrowthCtx }) {
  const { data, error, loading, reload } = useLoad(() => growth.ranking(ctx.campaign!.id), [ctx.campaign!.id], ctx.onNeedKey);
  if (loading && !data) return <LoadingBlock rows={3} />;
  if (error || !data) return <LoadError message={error ?? ''} onRetry={reload} />;
  const block = (title: string, rows: PostRow[], metric: (r: PostRow) => number | null | undefined, unit: string, empty: string) => (
    <Section title={title}>
      {rows.length === 0 ? (
        <p className="py-4 text-center text-[13px] text-fg-subtle">{empty}</p>
      ) : (
        <ol className="space-y-2">
          {rows.map((r, i) => (
            <li key={r.contentId} className="flex items-center gap-3 text-[13px]">
              <span className="w-4 text-right text-xs text-fg-subtle">{i + 1}</span>
              <span className="min-w-0 flex-1 truncate text-fg">{r.title || r.captionExcerpt}</span>
              <span className="text-xs text-fg-subtle">{KIND_LABEL[r.kind]}</span>
              <span className="w-20 text-right font-medium tabular-nums">{num(metric(r))} <span className="text-xs font-normal text-fg-subtle">{unit}</span></span>
            </li>
          ))}
        </ol>
      )}
    </Section>
  );
  const none = 'Chưa có bài có số liệu.';
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      {block('Top lượt xem', data.topViews, (r) => r.views, 'views', none)}
      {block('Top tiếp cận', data.topReach, (r) => r.reach, 'reach', none)}
      {block('Top chia sẻ', data.topShares, (r) => r.shares, 'shares', none)}
      {block('Top tương tác', data.topEngagement, (r) => r.engagement, '', none)}
      {block('Top mang lại follower', data.topFollows, (r) => r.follows, 'follow', data.followAttributionAvailable ? none : 'Meta không cung cấp follower theo từng bài cho Page này (chỉ có với Reels khi được cấp số liệu post_video_followers).')}
      {block('Xem nhiều nhưng không tạo follower', data.highViewsNoFollows, (r) => r.views, 'views', data.followAttributionAvailable ? 'Không có bài nào như vậy.' : 'Cần số liệu follower theo bài từ Meta.')}
      {data.weakest.length > 0 && block('Kém hiệu quả nhất', data.weakest, (r) => r.views, 'views', none)}
    </div>
  );
}

function HoursTab({ ctx }: { ctx: GrowthCtx }) {
  const { data, error, loading, reload } = useLoad(() => growth.hours(ctx.campaign!.id), [ctx.campaign!.id], ctx.onNeedKey);
  if (loading && !data) return <LoadingBlock rows={2} />;
  if (error || !data) return <LoadError message={error ?? ''} onRetry={reload} />;
  return (
    <Section title="Phân tích giờ đăng" description={`Lượt xem trung bình theo giờ đăng (giờ Việt Nam). Cơ sở: ${data.basis}.`}>
      {data.samples === 0 ? (
        <p className="py-6 text-center text-[13px] text-fg-subtle">Chưa có bài nào có số liệu lượt xem.</p>
      ) : (
        <>
          <HourColumns buckets={data.buckets} highlight={data.suggestedSlots} />
          {data.enoughData ? (
            data.suggestedSlots.length > 0 ? (
              <Alert tone="info" className="mt-4" title={`Khung giờ đề xuất: ${data.suggestedSlots.join(', ')}`}>
                Các giờ có lượt xem trung bình cao nhất trên Page của bạn (mỗi giờ ít nhất 2 bài). Đổi khung giờ ở Cài đặt chiến dịch nếu muốn — lịch đã duyệt không tự thay đổi.
              </Alert>
            ) : (
              <p className="mt-3 text-[13px] text-fg-muted">Chưa giờ nào có từ 2 bài trở lên — thử đăng thêm ở nhiều khung giờ.</p>
            )
          ) : (
            <p className="mt-3 text-[13px] text-fg-muted">Cần ít nhất {data.minSamples} bài có số liệu để đề xuất (hiện có {data.samples}). Không có giờ nào được mặc định là "tốt nhất".</p>
          )}
        </>
      )}
    </Section>
  );
}

function FollowersTab({ ctx }: { ctx: GrowthCtx }) {
  const campaign = ctx.campaign!;
  const toast = useToast();
  const { data, error, loading, reload } = useLoad(() => growth.followers(campaign.id), [campaign.id], ctx.onNeedKey);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [m, setM] = useState({ date: vnToday() < campaign.endDate ? vnToday() : campaign.endDate, invitesSent: '', invitesAccepted: '', pageVisits: '', newFollowers: '', note: '' });
  const [saving, setSaving] = useState(false);

  const toggle = async (key: string, done: boolean) => {
    setBusyKey(key);
    try {
      ctx.setCampaign(await growth.checklist(campaign.id, key, done));
    } catch (e) {
      toast.error('Không lưu được', (e as Error).message);
    } finally {
      setBusyKey(null);
    }
  };
  const saveManual = async () => {
    setSaving(true);
    const n = (s: string) => (s.trim() === '' ? null : Number(s));
    try {
      await growth.manualMetrics(campaign.id, { date: m.date, invitesSent: n(m.invitesSent), invitesAccepted: n(m.invitesAccepted), pageVisits: n(m.pageVisits), newFollowers: n(m.newFollowers), note: m.note });
      toast.success('Đã lưu số liệu nhập tay');
      reload();
    } catch (e) {
      toast.error('Không lưu được', (e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  if (loading && !data) return <LoadingBlock rows={3} />;
  if (error || !data) return <LoadError message={error ?? ''} onRetry={reload} />;
  const stat = (label: string, v: number | null | undefined, unit = '', note?: string | null) => (
    <div title={note ?? undefined}>
      <div className="text-xs text-fg-subtle">{label}</div>
      <div className={cn('text-xl font-semibold tabular-nums', v == null ? 'text-fg-subtle' : 'text-fg')}>{v == null ? '—' : num(v, unit)}</div>
    </div>
  );
  return (
    <div className="space-y-4">
      <Section title="Chuyển đổi follower" description="Follower Growth = Follower mới ÷ Follower đầu kỳ × 100%. Follower Conversion = Follower mới ÷ Lượt truy cập Page × 100%. Chỉ tính khi đủ dữ liệu.">
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
          {stat('Đầu kỳ', data.starting)}
          {stat('Hiện tại', data.current)}
          {stat('Follower mới', data.gain)}
          {stat('Tỷ lệ tăng', data.growthPercent, '%')}
          {stat('Truy cập Page', data.pageVisits)}
          {stat('Chuyển đổi', data.conversionPercent, '%', data.conversionNote)}
        </div>
        {data.conversionNote && <p className="mt-2 text-xs text-fg-subtle">{data.conversionNote}</p>}
        <div className="mt-5 grid gap-5 lg:grid-cols-2">
          <div>
            <div className="mb-2 text-[13px] font-medium text-fg">Follower mới theo ngày</div>
            <LineChart ariaLabel="Follower mới theo ngày" points={data.days.map((d) => ({ label: dm(d.date), value: d.newFollows ?? d.netChange, muted: d.date < campaign.startDate }))} empty="Chưa có số follower theo ngày từ Meta." />
          </div>
          <div>
            <div className="mb-2 text-[13px] font-medium text-fg">Tỷ lệ chuyển đổi theo ngày</div>
            <LineChart ariaLabel="Chuyển đổi theo ngày" unit="%" points={data.days.map((d) => ({ label: dm(d.date), value: d.conversionPercent, muted: d.date < campaign.startDate }))} empty="Cần follower mới và lượt truy cập Page theo ngày." />
          </div>
          <div>
            <div className="mb-2 text-[13px] font-medium text-fg">Loại nội dung hiệu quả nhất (tương tác TB)</div>
            <BarList items={data.byKind.filter((k) => k.kind !== 'Story').map((k) => ({ label: KIND_LABEL[k.kind], value: k.avgEngagement }))} empty="Chưa có số liệu bài đăng." />
            {data.bestKind && <p className="mt-2 text-xs text-fg-muted">Hiệu quả nhất hiện tại: <b>{KIND_LABEL[data.bestKind]}</b>.</p>}
          </div>
          <div>
            <div className="mb-2 text-[13px] font-medium text-fg">Lời mời theo dõi (Meta One, nhập tay)</div>
            <div className="grid grid-cols-3 gap-4">
              {stat('Đã gửi', data.invitesSent)}
              {stat('Chấp nhận', data.invitesAccepted)}
              {stat('Tỷ lệ', data.invitesSent && data.invitesAccepted != null ? (data.invitesAccepted * 100) / data.invitesSent : null, '%')}
            </div>
          </div>
        </div>
      </Section>

      <div className="grid gap-4 lg:grid-cols-2">
        <Section title="Checklist Meta One" description="Không có API để bật các tính năng Meta One — công cụ chỉ theo dõi và ghi nhận. Meta tự mời người đã tương tác theo dõi Page nếu tài khoản được cấp quyền.">
          <ul className="space-y-3">
            {campaign.checklist.map((i) => (
              <li key={i.key}>
                <button type="button" disabled={busyKey === i.key} onClick={() => toggle(i.key, !i.done)} className="flex w-full items-start gap-2.5 text-left">
                  {i.done ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-success" /> : <Circle className="mt-0.5 h-4 w-4 shrink-0 text-fg-subtle" />}
                  <span>
                    <span className={cn('block text-[13px]', i.done ? 'text-fg' : 'text-fg')}>{i.label}</span>
                    <span className="block text-xs text-fg-muted">{i.help}</span>
                    {i.atUtc && i.done && <span className="block text-[11px] text-fg-subtle">Đánh dấu {vnShort(i.atUtc)}</span>}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </Section>
        <Section title="Số liệu nhập tay" description="Những số Meta không cung cấp qua API (số lời mời, lượt truy cập xem trong ứng dụng). Luôn được đánh dấu là nhập tay.">
          <div className="space-y-3">
            <Field label="Ngày">
              <Input type="date" value={m.date} min={campaign.startDate} max={vnToday() < campaign.endDate ? vnToday() : campaign.endDate} onChange={(e) => setM({ ...m, date: e.target.value })} />
            </Field>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Lời mời đã gửi"><Input inputMode="numeric" value={m.invitesSent} onChange={(e) => setM({ ...m, invitesSent: e.target.value })} /></Field>
              <Field label="Lời mời chấp nhận"><Input inputMode="numeric" value={m.invitesAccepted} onChange={(e) => setM({ ...m, invitesAccepted: e.target.value })} /></Field>
              <Field label="Lượt truy cập Page"><Input inputMode="numeric" value={m.pageVisits} onChange={(e) => setM({ ...m, pageVisits: e.target.value })} /></Field>
              <Field label="Follower mới"><Input inputMode="numeric" value={m.newFollowers} onChange={(e) => setM({ ...m, newFollowers: e.target.value })} /></Field>
            </div>
            <Field label="Ghi chú" optional><Textarea rows={2} value={m.note} onChange={(e) => setM({ ...m, note: e.target.value })} /></Field>
            <Button variant="primary" loading={saving} onClick={saveManual}>Lưu</Button>
          </div>
        </Section>
      </div>
    </div>
  );
}

function ReportTab({ ctx }: { ctx: GrowthCtx }) {
  const campaign = ctx.campaign!;
  const toast = useToast();
  const { data, error, loading, reload, setData } = useLoad(() => growth.report(campaign.id), [campaign.id], ctx.onNeedKey);
  const [busy, setBusy] = useState<string | null>(null);
  const generate = async (ai: boolean) => {
    setBusy(ai ? 'ai' : 'plain');
    try {
      setData(await growth.generateReport(campaign.id, ai));
      if (ai) ctx.reloadCampaigns();
      toast.success('Đã tạo báo cáo');
    } catch (e) {
      toast.error('Không tạo được báo cáo', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };
  const download = async (f: 'csv' | 'xls') => {
    try {
      await growth.downloadReport(campaign.id, f);
    } catch (e) {
      toast.error('Không tải được', (e as Error).message);
    }
  };
  if (loading && data === null) return <LoadingBlock rows={3} />;
  if (error) return <LoadError message={error} onRetry={reload} />;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap gap-2 print:hidden">
        <Button variant="primary" loading={busy === 'plain'} onClick={() => generate(false)}>{data ? 'Tạo lại báo cáo' : 'Tạo báo cáo'}</Button>
        <Button icon={<Sparkles className="h-4 w-4" />} loading={busy === 'ai'} onClick={() => generate(true)}>Tạo kèm nhận xét AI</Button>
        <div className="flex-1" />
        <Button variant="ghost" icon={<FileDown className="h-4 w-4" />} onClick={() => download('csv')}>CSV</Button>
        <Button variant="ghost" icon={<FileDown className="h-4 w-4" />} onClick={() => download('xls')}>Excel</Button>
        <Button variant="ghost" icon={<Printer className="h-4 w-4" />} disabled={!data} onClick={() => window.print()}>In / Lưu PDF</Button>
      </div>
      {!data ? (
        <p className="rounded-lg border border-dashed border-line py-10 text-center text-[13px] text-fg-subtle">
          Chưa có báo cáo. Báo cáo được tạo tự động khi chiến dịch kết thúc, hoặc bấm "Tạo báo cáo" bất cứ lúc nào.
        </p>
      ) : (
        <ReportView r={data} />
      )}
    </div>
  );
}

function ReportView({ r }: { r: Report }) {
  const d = r.data;
  const tone = r.verdict === 'Continue' ? 'success' : r.verdict === 'TestMore' ? 'info' : r.verdict === 'DontBuy' ? 'warning' : 'neutral';
  const row = (label: string, value: string) => (
    <tr className="border-b border-line last:border-0"><td className="py-2 pr-4 text-fg-muted">{label}</td><td className="py-2 text-right font-medium tabular-nums text-fg">{value}</td></tr>
  );
  const money = (v?: number | null, cur = 'USD') => (v == null ? 'Chưa có dữ liệu' : `${v.toLocaleString('vi-VN', { maximumFractionDigits: cur === 'VND' ? 0 : 4 })} ${cur}`);
  return (
    <div className="space-y-4">
      <Section title={`Báo cáo: ${d.campaignName}`} description={`${d.pageName}${d.pageId ? ` · ID ${d.pageId}` : ''} · ${dm(d.startDate)} – ${dm(d.endDate)} · tạo lúc ${vnShort(r.createdAtUtc)}`}>
        <Alert tone={tone} title={`Đánh giá Meta One: ${r.verdictLabel}`}>{r.verdictReason}</Alert>
        {r.aiCommentary && <p className="mt-3 whitespace-pre-wrap rounded-md bg-subtle p-3 text-[13px] text-fg">{r.aiCommentary}</p>}
      </Section>
      <div className="grid gap-4 lg:grid-cols-2">
        <Section title="Kết quả">
          <table className="w-full text-[13px]"><tbody>
            {row('Follower đầu kỳ', num(d.followersStart))}
            {row('Follower cuối kỳ', num(d.followersEnd))}
            {row('Tổng follower tăng', num(d.followerGain))}
            {row('Tỷ lệ tăng', num(d.growthPercent, '%'))}
            {row('Tổng lượt xem', num(d.totalViews))}
            {row('Tổng reach', num(d.totalReach))}
            {row('Tổng tương tác', num(d.totalEngagement))}
            {row('Tổng số bài đăng', String(d.totalPublished))}
          </tbody></table>
        </Section>
        <Section title="Có nên mua Meta One tiếp?" description="Chỉ là so sánh số liệu — không kết luận Meta One tạo ra toàn bộ follower tăng thêm.">
          <table className="w-full text-[13px]"><tbody>
            {row('Follow mới 7 ngày trước', num(d.before?.newFollows))}
            {row('Follow mới trong chiến dịch', num(d.during?.newFollows))}
            {row('Mức tăng follower', num(d.followerGain))}
            {row('Lời mời Meta One gửi / chấp nhận', `${num(d.metaOneInvitesSent)} / ${num(d.metaOneInvitesAccepted)}`)}
            {row('Chi phí Meta One', money(d.metaOneCost, d.metaOneCurrency))}
            {row('Chi phí sản xuất nội dung', money(d.contentCostUsd))}
            {row('Nội dung / follower mới', money(d.contentCostPerFollowerUsd))}
            {row('Meta One / follower mới', money(d.metaOneCostPerFollower, d.metaOneCurrency))}
          </tbody></table>
        </Section>
        <Section title="Hiệu quả theo loại">
          <table className="w-full text-[13px]">
            <thead><tr className="text-left text-xs text-fg-subtle"><th className="pb-1 font-medium">Loại</th><th className="pb-1 text-right font-medium">Bài</th><th className="pb-1 text-right font-medium">Views</th><th className="pb-1 text-right font-medium">Tương tác</th><th className="pb-1 text-right font-medium">TB/bài</th></tr></thead>
            <tbody>
              {d.byKind.map((k) => (
                <tr key={k.kind} className="border-t border-line"><td className="py-1.5">{KIND_LABEL[k.kind]}</td><td className="text-right tabular-nums">{k.published}</td><td className="text-right tabular-nums">{num(k.views)}</td><td className="text-right tabular-nums">{num(k.engagement)}</td><td className="text-right tabular-nums">{num(k.avgEngagement)}</td></tr>
              ))}
            </tbody>
          </table>
          {d.byKind.length === 0 && <p className="py-3 text-center text-[13px] text-fg-subtle">Chưa có bài đăng.</p>}
        </Section>
        <Section title="Khung giờ tốt nhất">
          <p className="text-[13px] text-fg">{d.bestSlots.length > 0 ? d.bestSlots.join(', ') : '—'}</p>
          <p className="mt-1 text-xs text-fg-subtle">{d.bestSlotsBasis}</p>
          <div className="mt-3 text-xs text-fg-subtle">Chi phí AI {money(d.aiCostUsd)} · video AI {money(d.videoCostUsd)}</div>
        </Section>
      </div>
      <Section title="Top 5 nội dung tốt nhất"><PostTable rows={d.top} compact /></Section>
      {d.weak.length > 0 && <Section title="Nội dung kém hiệu quả"><PostTable rows={d.weak} compact /></Section>}
      <div className="grid gap-4 lg:grid-cols-2">
        <Section title="Đề xuất tháng tiếp theo">
          <ul className="space-y-1.5 text-[13px] text-fg">{d.recommendations.map((t, i) => <li key={i} className="flex gap-2"><span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-accent" />{t}</li>)}</ul>
        </Section>
        <Section title="Dữ liệu còn thiếu" description="Để kết luận chắc chắn hơn.">
          {d.dataGaps.length === 0 ? <p className="text-[13px] text-fg-subtle">Không thiếu dữ liệu quan trọng.</p> : <ul className="space-y-1.5 text-[13px] text-fg-muted">{d.dataGaps.map((t, i) => <li key={i}>• {t}</li>)}</ul>}
        </Section>
      </div>
      <Section title="Meta One — checklist">
        <ul className="grid gap-1.5 text-[13px] sm:grid-cols-2">{d.metaOneChecklist.map((i) => <li key={i.key} className="flex items-center gap-2">{i.done ? <CheckCircle2 className="h-4 w-4 text-success" /> : <Circle className="h-4 w-4 text-fg-subtle" />}{i.label}</li>)}</ul>
      </Section>
    </div>
  );
}

function LogsTab({ ctx }: { ctx: GrowthCtx }) {
  const { data, error, loading, reload } = useLoad(() => growth.logs(ctx.campaign!.id), [ctx.campaign!.id], ctx.onNeedKey);
  if (loading && !data) return <LoadingBlock rows={2} />;
  if (error || !data) return <LoadError message={error ?? ''} onRetry={reload} />;
  return (
    <Section title="Nhật ký hoạt động" description="Mọi thay đổi, lần đăng, lỗi và lần dùng AI của chiến dịch." actions={<Button size="sm" variant="ghost" onClick={reload}>Làm mới</Button>}>
      {data.length === 0 ? (
        <p className="py-6 text-center text-[13px] text-fg-subtle">Chưa có hoạt động.</p>
      ) : (
        <ul className="divide-y divide-line">
          {data.map((l) => (
            <li key={l.id} className="flex gap-3 py-2 text-[13px]">
              <span className="w-24 shrink-0 text-xs tabular-nums text-fg-subtle">{vnShort(l.createdAtUtc)}</span>
              <span className={cn('mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full', l.level === 'Error' ? 'bg-danger' : l.level === 'Warning' ? 'bg-warning' : 'bg-fg-subtle')} />
              <span className="min-w-0 flex-1 text-fg">{l.message}</span>
              <span className="shrink-0 text-[11px] text-fg-subtle">{l.actor === 'scheduler' ? 'tự động' : l.actor === 'system' ? 'hệ thống' : 'bạn'}</span>
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}

