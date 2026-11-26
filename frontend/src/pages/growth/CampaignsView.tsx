import { useState } from 'react';
import { Plus, Trash2, Rocket } from 'lucide-react';
import { Alert, Button, Field, Input, Modal, Segmented, Textarea } from '../../components/ui';
import { useConfirm, useToast } from '../../components/feedback';
import { navigate } from '../../lib/router';
import { growth, type Campaign, type CampaignInput, type PlanDay } from '../../services/growth';
import { addDays, dm, num, vnToday } from '../../lib/growthMath';
import { CampaignBadge, Section, type GrowthCtx } from './shared';

export function CampaignsView({ ctx }: { ctx: GrowthCtx }) {
  const [creating, setCreating] = useState(false);
  const toast = useToast();
  const confirm = useConfirm();

  if (!ctx.page) return <p className="text-[13px] text-fg-muted">Chọn một Page ở phía trên.</p>;

  const remove = async (c: Campaign) => {
    if (!(await confirm({ title: `Xoá chiến dịch "${c.name}"?`, description: 'Chỉ xoá được chiến dịch chưa bắt đầu hoặc đã huỷ và chưa có bài đăng.', danger: true, confirmLabel: 'Xoá' }))) return;
    try {
      await growth.deleteCampaign(c.id);
      toast.success('Đã xoá chiến dịch');
      if (ctx.campaign?.id === c.id) ctx.selectCampaign(null);
      ctx.reloadCampaigns();
    } catch (e) {
      toast.error('Không xoá được', (e as Error).message);
    }
  };

  return (
    <div className="space-y-5">
      <Section
        title={`Chiến dịch của ${ctx.page.pageName}`}
        description="Mỗi Page chạy một chiến dịch tại một thời điểm."
        actions={<Button size="sm" variant="primary" icon={<Plus className="h-3.5 w-3.5" />} onClick={() => setCreating(true)}>Tạo chiến dịch</Button>}
      >
        {ctx.campaigns.length === 0 ? (
          <div className="py-8 text-center">
            <p className="text-[13px] text-fg-muted">Chưa có chiến dịch.</p>
            <Button className="mt-3" variant="primary" icon={<Rocket className="h-4 w-4" />} onClick={() => setCreating(true)}>Tạo "Meta One 7-Day Growth Challenge"</Button>
          </div>
        ) : (
          <div className="-mx-1 divide-y divide-line">
            {ctx.campaigns.map((c) => (
              <div key={c.id} className="flex flex-col gap-2 px-1 py-3 sm:flex-row sm:items-center">
                <button type="button" className="min-w-0 flex-1 text-left" onClick={() => { ctx.selectCampaign(c.id); navigate('/growth/overview'); }}>
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-[14px] font-medium text-fg hover:underline">{c.name}</span>
                    <CampaignBadge status={c.status} />
                    {ctx.campaign?.id === c.id && <span className="text-xs text-accent">đang xem</span>}
                  </div>
                  <div className="mt-0.5 text-xs text-fg-subtle">
                    {dm(c.startDate)} – {dm(c.endDate)} · {c.reelsPerDay} Reels, {c.photosPerDay} ảnh, {c.storiesPerDay} Story/ngày · {c.approvalMode === 'Auto' ? 'Tự động đăng' : 'Duyệt trước'}
                    {c.targetFollowers ? ` · mục tiêu ${num(c.targetFollowers)} follower` : ''} · {c.contentCounts.Published ?? 0} bài đã đăng
                  </div>
                </button>
                <div className="flex gap-2">
                  <Button size="sm" variant="ghost" onClick={() => { ctx.selectCampaign(c.id); navigate('/growth/settings'); }}>Cài đặt</Button>
                  {(c.status === 'Draft' || c.status === 'Cancelled') && (
                    <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} onClick={() => remove(c)}>Xoá</Button>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </Section>

      {ctx.campaign && <PlanEditor ctx={ctx} campaign={ctx.campaign} />}

      <Modal open={creating} onClose={() => setCreating(false)} title="Tạo chiến dịch" description="Mẫu mặc định: Meta One 7-Day Growth Challenge — tuỳ chỉnh được." size="lg">
        <CampaignForm
          ctx={ctx}
          onDone={(c) => {
            setCreating(false);
            ctx.setCampaign(c);
            ctx.selectCampaign(c.id);
            navigate('/growth/overview');
          }}
        />
      </Modal>
    </div>
  );
}

/** Create (no campaign) or edit settings of a campaign. */
export function CampaignForm({ ctx, campaign, onDone }: { ctx: GrowthCtx; campaign?: Campaign; onDone: (c: Campaign) => void }) {
  const toast = useToast();
  const today = vnToday();
  const [f, setF] = useState(() => ({
    name: campaign?.name ?? 'Meta One 7-Day Growth Challenge',
    startDate: campaign?.startDate ?? today,
    days: campaign?.totalDays ?? 7,
    targetFollowers: campaign?.targetFollowers?.toString() ?? '',
    targetViews: campaign?.targetViews?.toString() ?? '',
    reelsPerDay: campaign?.reelsPerDay ?? 1,
    photosPerDay: campaign?.photosPerDay ?? 2,
    storiesPerDay: campaign?.storiesPerDay ?? 3,
    timeSlots: (campaign?.timeSlots ?? ['07:30', '11:30', '20:00']).join(', '),
    aiBudgetUsd: campaign?.aiBudgetUsd ?? 1,
    approvalMode: campaign?.approvalMode ?? ('Review' as 'Auto' | 'Review'),
    metaOneCost: campaign?.metaOneCost?.toString() ?? '',
    metaOneCurrency: campaign?.metaOneCurrency ?? ('VND' as 'VND' | 'USD'),
    notes: campaign?.notes ?? '',
  }));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = <K extends keyof typeof f>(k: K, v: (typeof f)[K]) => setF((x) => ({ ...x, [k]: v }));
  const started = campaign && campaign.status !== 'Draft';
  const ended = campaign && (campaign.status === 'Completed' || campaign.status === 'Cancelled');

  const submit = async () => {
    setBusy(true);
    setError(null);
    const numOrNull = (s: string) => (s.trim() === '' ? null : Number(s.replace(/[.,\s]/g, '')));
    const input: CampaignInput = {
      name: f.name,
      startDate: started ? undefined : f.startDate,
      endDate: addDays(started ? campaign!.startDate : f.startDate, Number(f.days) - 1),
      targetFollowers: numOrNull(f.targetFollowers) ?? 0,
      targetViews: numOrNull(f.targetViews) ?? 0,
      reelsPerDay: Number(f.reelsPerDay),
      photosPerDay: Number(f.photosPerDay),
      storiesPerDay: Number(f.storiesPerDay),
      timeSlots: f.timeSlots.split(/[,\s]+/).filter(Boolean),
      aiBudgetUsd: Number(f.aiBudgetUsd),
      approvalMode: f.approvalMode,
      metaOneCost: numOrNull(f.metaOneCost) ?? undefined,
      metaOneCurrency: f.metaOneCurrency,
      notes: f.notes,
    };
    try {
      const c = campaign ? await growth.updateCampaign(campaign.id, input) : await growth.createCampaign({ ...input, facebookAccountId: ctx.page!.id });
      toast.success(campaign ? 'Đã lưu cài đặt' : 'Đã tạo chiến dịch');
      onDone(c);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-4">
      {error && <Alert tone="danger">{error}</Alert>}
      {ended && <Alert tone="info">Chiến dịch đã kết thúc — không sửa được nữa.</Alert>}
      <fieldset disabled={!!ended} className="space-y-4">
        <Field label="Tên chiến dịch">
          <Input value={f.name} onChange={(e) => set('name', e.target.value)} maxLength={200} />
        </Field>
        <div className="grid gap-4 sm:grid-cols-3">
          <Field label="Page">
            <Input value={campaign?.pageName ?? ctx.page?.pageName ?? ''} disabled />
          </Field>
          <Field label="Ngày bắt đầu" hint={started ? 'Đã bắt đầu — không đổi được.' : undefined}>
            <Input type="date" value={started ? campaign!.startDate : f.startDate} min={today} disabled={!!started} onChange={(e) => set('startDate', e.target.value)} />
          </Field>
          <Field label="Số ngày" hint={`Kết thúc ${dm(addDays(started ? campaign!.startDate : f.startDate, Number(f.days) - 1))}`}>
            <Input type="number" min={1} max={31} value={f.days} onChange={(e) => set('days', Number(e.target.value))} />
          </Field>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Mục tiêu follower mới" optional>
            <Input inputMode="numeric" value={f.targetFollowers} onChange={(e) => set('targetFollowers', e.target.value)} placeholder="ví dụ 500" />
          </Field>
          <Field label="Mục tiêu lượt xem" optional>
            <Input inputMode="numeric" value={f.targetViews} onChange={(e) => set('targetViews', e.target.value)} placeholder="ví dụ 100000" />
          </Field>
        </div>
        <div className="grid grid-cols-3 gap-4">
          <Field label="Reels / ngày"><Input type="number" min={0} max={5} value={f.reelsPerDay} onChange={(e) => set('reelsPerDay', Number(e.target.value))} /></Field>
          <Field label="Ảnh / ngày"><Input type="number" min={0} max={10} value={f.photosPerDay} onChange={(e) => set('photosPerDay', Number(e.target.value))} /></Field>
          <Field label="Story / ngày"><Input type="number" min={0} max={20} value={f.storiesPerDay} onChange={(e) => set('storiesPerDay', Number(e.target.value))} /></Field>
        </div>
        <p className="-mt-2 text-xs text-fg-subtle">Đây là tần suất đề xuất — không bắt buộc đăng đủ nếu không có nội dung tốt.</p>
        <Field label="Khung giờ đăng (giờ Việt Nam)" hint="Dạng HH:mm, cách nhau bằng dấu phẩy. Không có giờ nào được coi là 'tốt nhất' sẵn — xem Phân tích → Giờ đăng sau vài ngày.">
          <Input value={f.timeSlots} onChange={(e) => set('timeSlots', e.target.value)} />
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Chế độ đăng">
            <Segmented options={[{ value: 'Review', label: 'Duyệt trước' }, { value: 'Auto', label: 'Tự động' }]} value={f.approvalMode} onChange={(v) => set('approvalMode', v)} />
          </Field>
          <Field label="Ngân sách AI (USD)" hint={campaign ? `Đã dùng ${campaign.aiSpentUsd.toFixed(3)} USD` : 'Chỉ cho caption / phân tích. Không chạy quảng cáo.'}>
            <Input type="number" min={0} max={50} step={0.1} value={f.aiBudgetUsd} onChange={(e) => set('aiBudgetUsd', Number(e.target.value))} />
          </Field>
        </div>
        <div className="grid gap-4 sm:grid-cols-[1fr_120px]">
          <Field label="Chi phí Meta One (để tính chi phí / follower)" optional>
            <Input inputMode="numeric" value={f.metaOneCost} onChange={(e) => set('metaOneCost', e.target.value)} placeholder="Giá thực trả cho kỳ này" />
          </Field>
          <Field label="Tiền tệ">
            <Segmented size="sm" options={[{ value: 'VND', label: 'VND' }, { value: 'USD', label: 'USD' }]} value={f.metaOneCurrency} onChange={(v) => set('metaOneCurrency', v)} />
          </Field>
        </div>
        <Field label="Ghi chú" optional>
          <Textarea rows={2} value={f.notes} onChange={(e) => set('notes', e.target.value)} maxLength={4000} />
        </Field>
      </fieldset>
      {!ended && (
        <div className="flex justify-end">
          <Button variant="primary" loading={busy} onClick={submit}>{campaign ? 'Lưu cài đặt' : 'Tạo chiến dịch'}</Button>
        </div>
      )}
    </div>
  );
}

/** The day-by-day plan (suggested template, editable). Changing it never changes approved posts. */
function PlanEditor({ ctx, campaign }: { ctx: GrowthCtx; campaign: Campaign }) {
  const toast = useToast();
  const [plan, setPlan] = useState<PlanDay[]>(campaign.plan);
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const ended = campaign.status === 'Completed' || campaign.status === 'Cancelled';
  const update = (i: number, p: Partial<PlanDay>) => {
    setPlan((list) => list.map((d, j) => (j === i ? { ...d, ...p } : d)));
    setDirty(true);
  };
  const save = async () => {
    setBusy(true);
    try {
      ctx.setCampaign(await growth.updateCampaign(campaign.id, { plan }));
      setDirty(false);
      toast.success('Đã lưu kế hoạch');
    } catch (e) {
      toast.error('Không lưu được', (e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Section
      title={`Kế hoạch: ${campaign.name}`}
      description="Lịch nội dung đề xuất theo ngày. Sửa kế hoạch không làm thay đổi bài đã duyệt."
      actions={dirty && <Button size="sm" variant="primary" loading={busy} onClick={save}>Lưu kế hoạch</Button>}
    >
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {plan.map((d, i) => (
          <div key={d.day} className={`rounded-md border p-3 ${campaign.dayNumber === d.day && campaign.status === 'Running' ? 'border-accent/50 bg-accent/[0.04]' : 'border-line'}`}>
            <div className="mb-1.5 flex items-center justify-between text-xs text-fg-subtle">
              <span className="font-semibold uppercase tracking-wide">Ngày {d.day}</span>
              {d.date && <span className="tabular-nums">{dm(d.date)}</span>}
            </div>
            <Input value={d.focus} disabled={ended} onChange={(e) => update(i, { focus: e.target.value })} className="mb-2 font-medium" aria-label={`Trọng tâm ngày ${d.day}`} />
            <Textarea rows={4} disabled={ended} value={d.tasks.join('\n')} onChange={(e) => update(i, { tasks: e.target.value.split('\n') })} aria-label={`Việc ngày ${d.day}`} className="text-[13px]" />
          </div>
        ))}
      </div>
    </Section>
  );
}
