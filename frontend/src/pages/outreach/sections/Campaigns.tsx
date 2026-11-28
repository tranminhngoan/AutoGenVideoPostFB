import { useMemo, useState } from 'react';
import { CalendarRange, Check, ExternalLink, ImageIcon, ListChecks, Megaphone, Pencil, Plus, Share2, Trash2, UsersRound } from 'lucide-react';
import { Badge, Button, Card, Checkbox, Drawer, EmptyState, Field, Input, Modal, ProgressBar, Select, Skeleton, Textarea } from '../../../components/ui';
import { useConfirm, useToast } from '../../../components/feedback';
import { href } from '../../../lib/router';
import { Campaign, CampaignInput, CampaignLink, CampaignStatus, Group, outreach, OutreachPage, STATUS_LABEL } from '../outreachApi';
import { fmtDate, fmtDateTime, PageAvatar, rate, useLoad } from '../shared';

const STATUS_TONE: Record<CampaignStatus, 'neutral' | 'warning' | 'success' | 'info' | 'danger'> = {
  Draft: 'neutral', PendingApproval: 'warning', Running: 'success', Paused: 'info', Completed: 'neutral',
};
function CampaignForm({ open, initial, pages, groups, onClose, onSaved }: {
  open: boolean; initial: Campaign | null; pages: OutreachPage[]; groups: Group[]; onClose: () => void; onSaved: (c: Campaign) => void;
}) {
  const toast = useToast();
  const { data: library } = useLoad(() => (open ? outreach.contents() : Promise.resolve([])), [open]);
  const [form, setForm] = useState<CampaignInput>({ name: '' });
  const [groupFilter, setGroupFilter] = useState('');
  const [busy, setBusy] = useState(false);
  const [lastOpen, setLastOpen] = useState(false);
  if (open !== lastOpen) {
    setLastOpen(open);
    if (open)
      setForm(initial
        ? { ...initial, startDate: initial.startDate?.slice(0, 10) ?? null, endDate: initial.endDate?.slice(0, 10) ?? null }
        : { name: '', status: 'Draft', pageIds: pages.filter((p) => p.isHelper && p.health !== 'error').map((p) => p.id), groupIds: [] });
  }
  const set = (p: Partial<CampaignInput>) => setForm((f) => ({ ...f, ...p }));
  const toggle = (key: 'pageIds' | 'groupIds', id: string) => {
    const list = form[key] ?? [];
    set({ [key]: list.includes(id) ? list.filter((x) => x !== id) : [...list, id] });
  };
  const helpers = pages.filter((p) => p.isHelper);
  const shownGroups = groups.filter((g) => g.fit !== 'unfit' && (!groupFilter || g.name.toLowerCase().includes(groupFilter.toLowerCase()) || (g.topic ?? '').toLowerCase().includes(groupFilter.toLowerCase())));

  const save = async () => {
    setBusy(true);
    try {
      const body: CampaignInput = { ...form, slug: form.slug || undefined, contentId: form.contentId || null, startDate: form.startDate || null, endDate: form.endDate || null };
      const c = initial ? await outreach.editCampaign(initial.id, body) : await outreach.addCampaign(body);
      toast.success(initial ? 'Đã lưu chiến dịch' : 'Đã tạo chiến dịch');
      onSaved(c);
      onClose();
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal open={open} onClose={onClose} size="lg" title={initial ? 'Sửa chiến dịch' : 'Tạo chiến dịch'}
      description="Chọn Page phụ và nhóm mục tiêu. Bài đăng lên tường Page rồi chia sẻ vào các nhóm này."
      footer={<><Button variant="ghost" onClick={onClose}>Huỷ</Button><Button variant="primary" loading={busy} disabled={!form.name.trim()} onClick={save}>Lưu</Button></>}>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Tên chiến dịch" className="sm:col-span-2">
          <Input value={form.name} onChange={(e) => set({ name: e.target.value })} placeholder="VD: Kéo follow tháng 12" />
        </Field>
        {!initial && (
          <Field label="Mã chiến dịch (utm_campaign)" optional hint="Để trống: tự đặt campaign_01, campaign_02…">
            <Input value={form.slug ?? ''} onChange={(e) => set({ slug: e.target.value })} placeholder="campaign_01" />
          </Field>
        )}
        <Field label="Trạng thái">
          <Select value={form.status ?? 'Draft'} onChange={(e) => set({ status: e.target.value as CampaignStatus })}>
            {(Object.keys(STATUS_LABEL) as CampaignStatus[]).map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
          </Select>
        </Field>
        <Field label="Người phụ trách" optional>
          <Input value={form.owner ?? ''} onChange={(e) => set({ owner: e.target.value })} />
        </Field>
        <Field label="Bắt đầu" optional>
          <Input type="date" value={form.startDate ?? ''} onChange={(e) => set({ startDate: e.target.value })} />
        </Field>
        <Field label="Kết thúc" optional>
          <Input type="date" value={form.endDate ?? ''} onChange={(e) => set({ endDate: e.target.value })} />
        </Field>

        <Field label="Page phụ dùng trong chiến dịch" className="sm:col-span-2">
          <div className="grid gap-2 sm:grid-cols-2">
            {helpers.map((p) => (
              <label key={p.id} className={`flex cursor-pointer items-center gap-2.5 rounded-lg border p-2.5 ${(form.pageIds ?? []).includes(p.id) ? 'border-rose-500/50 bg-rose-500/[0.04]' : 'border-line'}`}>
                <input type="checkbox" checked={(form.pageIds ?? []).includes(p.id)} onChange={() => toggle('pageIds', p.id)} />
                <PageAvatar url={p.pictureUrl} name={p.name} size={28} />
                <span className="min-w-0 flex-1 truncate text-sm text-fg">{p.name}</span>
                {p.health !== 'ok' && <Badge tone={p.health === 'error' ? 'danger' : 'warning'}>{p.health === 'error' ? 'Mất quyền' : 'Chú ý'}</Badge>}
              </label>
            ))}
          </div>
        </Field>

        <Field label={`Group mục tiêu (${(form.groupIds ?? []).length} đã chọn)`} className="sm:col-span-2">
          <Input value={groupFilter} onChange={(e) => setGroupFilter(e.target.value)} placeholder="Lọc theo tên / chủ đề…" />
          <div className="mt-2 max-h-48 space-y-1 overflow-y-auto rounded-lg border border-line p-2">
            {shownGroups.length === 0 && <p className="p-2 text-xs text-fg-muted">Chưa có Group phù hợp — thêm ở Group Finder / Danh sách Group.</p>}
            {shownGroups.map((g) => (
              <Checkbox key={g.id} checked={(form.groupIds ?? []).includes(g.id)} onChange={() => toggle('groupIds', g.id)}
                label={<span>{g.name} {g.fit === 'fit' && <Badge tone="success">Phù hợp</Badge>}</span>} description={g.topic ?? undefined} />
            ))}
          </div>
        </Field>

        <Field label="Nội dung quảng bá" className="sm:col-span-2" hint="Ghi chú nội dung chính của chiến dịch (bài đăng thật tạo ở AI Outreach → Bài đăng ảnh chữ).">
          <Select value={form.contentId ?? ''} onChange={(e) => {
            const c = (library ?? []).find((x) => x.id === e.target.value);
            set({ contentId: c?.id ?? null, contentText: c?.text ?? form.contentText });
          }}>
            <option value="">— Chọn từ thư viện —</option>
            {(library ?? []).map((c) => <option key={c.id} value={c.id}>{c.text.slice(0, 90)}</option>)}
          </Select>
          <Textarea rows={3} className="mt-2" value={form.contentText ?? ''} onChange={(e) => set({ contentText: e.target.value })} />
        </Field>
        <Field label="Ghi chú kết quả" optional className="sm:col-span-2">
          <Textarea rows={2} value={form.resultNote ?? ''} onChange={(e) => set({ resultNote: e.target.value })} />
        </Field>
      </div>
    </Modal>
  );
}

function ProgressDrawer({ campaign, onClose }: { campaign: Campaign | null; onClose: () => void }) {
  const { data: rows, loading } = useLoad(() => (campaign ? outreach.links(campaign.id) : Promise.resolve([] as CampaignLink[])), [campaign?.id]);
  const list = rows ?? [];
  const shared = list.filter((r) => r.shares > 0 || r.activities.length > 0).length;

  return (
    <Drawer open={campaign != null} onClose={onClose} title={campaign?.name ?? ''} subtitle={`Tiến độ chia sẻ bài vào nhóm · ${shared}/${list.length} nhóm × Page`}>
      <div className="mb-3 flex flex-wrap gap-2">
        <a href={href('/outreach/ai')}><Button size="sm" variant="primary" icon={<ImageIcon className="h-3.5 w-3.5" />}>1. Tạo & đăng bài lên tường Page</Button></a>
        <a href={href('/outreach/share')}><Button size="sm" variant="secondary" icon={<Share2 className="h-3.5 w-3.5" />}>2. Chia sẻ vào nhóm</Button></a>
      </div>
      {loading && !rows ? <Skeleton className="h-40 w-full" /> : list.length === 0 ? (
        <EmptyState icon={<UsersRound />} title="Chiến dịch chưa có Group" description="Bấm Sửa chiến dịch để chọn Group mục tiêu." />
      ) : (
        <div className="divide-y divide-line rounded-lg border border-line">
          {list.map((row) => {
            const done = row.shares > 0 || row.activities.length > 0;
            return (
              <div key={row.utmContent} className="flex flex-wrap items-center gap-2 px-3 py-2.5">
                <div className="min-w-0 flex-1">
                  <a href={row.groupUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-sm font-medium text-fg hover:text-accent">
                    {row.groupName} <ExternalLink className="h-3 w-3" />
                  </a>
                  {row.pageName && <p className="text-xs text-fg-subtle">qua {row.pageName}</p>}
                </div>
                {row.pageName && (row.membership === 'joined'
                  ? <Badge tone="success">Page đã vào nhóm</Badge>
                  : <Badge tone="warning">{row.membership === 'requested' ? 'Page chờ duyệt' : row.membership === 'rejected' ? 'Page bị từ chối' : 'Page chưa vào nhóm'}</Badge>)}
                {done
                  ? <Badge tone="success" dot><Check className="h-3 w-3" />Đã chia sẻ{row.shares > 1 ? ` ${row.shares} lần` : ''}{row.lastSharedAtUtc ? ` · ${fmtDateTime(row.lastSharedAtUtc)}` : ''}</Badge>
                  : <Badge>Chưa chia sẻ</Badge>}
              </div>
            );
          })}
        </div>
      )}
      <p className="mt-3 text-xs text-fg-subtle">
        Tích “Đã chia sẻ” ở mục Chia sẻ vào nhóm (bài phải được đăng kèm chiến dịch này) — tiến độ ở đây tự cập nhật.
      </p>
    </Drawer>
  );
}

export function CampaignsSection() {
  const toast = useToast();
  const confirm = useConfirm();
  const { data: campaigns, setData, loading, reload } = useLoad(() => outreach.campaigns());
  const { data: pages } = useLoad(() => outreach.pages());
  const { data: groups } = useLoad(() => outreach.groups());
  const [editing, setEditing] = useState<Campaign | null>(null);
  const [adding, setAdding] = useState(false);
  const [linksOf, setLinksOf] = useState<Campaign | null>(null);
  const [status, setStatus] = useState('all');

  const pageName = useMemo(() => Object.fromEntries((pages ?? []).map((p) => [p.id, p.name])), [pages]);
  const list = (campaigns ?? []).filter((c) => status === 'all' || c.status === status);

  const remove = async (c: Campaign) => {
    if (!(await confirm({ title: `Xoá chiến dịch "${c.name}"?`, description: 'Các đánh dấu đã làm của chiến dịch cũng bị xoá. Số liệu lượt xem / bấm vẫn giữ.', confirmLabel: 'Xoá', danger: true }))) return;
    try {
      await outreach.deleteCampaign(c.id);
      setData((l) => l?.filter((x) => x.id !== c.id) ?? null);
    } catch (e) {
      toast.error('Chưa xoá được', (e as Error).message);
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <Select size="sm" className="w-auto" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="all">Mọi trạng thái</option>
          {(Object.keys(STATUS_LABEL) as CampaignStatus[]).map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
        </Select>
        <div className="flex-1" />
        <Button variant="primary" icon={<Plus className="h-4 w-4" />} onClick={() => setAdding(true)}>Tạo chiến dịch</Button>
      </div>

      {loading && !campaigns ? <Skeleton className="h-48 w-full" /> : list.length === 0 ? (
        <EmptyState icon={<Megaphone />} title="Chưa có chiến dịch" description="Tạo chiến dịch: chọn Page phụ và nhóm mục tiêu, rồi đăng bài lên tường Page và chia sẻ vào các nhóm đó." />
      ) : (
        <div className="grid gap-3 lg:grid-cols-2">
          {list.map((c) => (
            <Card key={c.id} className="space-y-3 p-4">
              <div className="flex items-start gap-2">
                <div className="min-w-0 flex-1">
                  <p className="truncate font-semibold text-fg">{c.name}</p>
                  <p className="font-mono text-[11px] text-fg-subtle">{c.slug}</p>
                </div>
                <Badge tone={STATUS_TONE[c.status]} dot={c.status === 'Running'} pulse={c.status === 'Running'}>{STATUS_LABEL[c.status]}</Badge>
              </div>
              <div className="rounded-lg border border-rose-500/20 bg-rose-500/[0.03] p-2.5">
                <div className="grid grid-cols-2 gap-2 text-center">
                  <div>
                    <p className="text-2xl font-semibold tabular-nums text-fg">{c.views}</p>
                    <p className="text-[11px] text-fg-muted">Lượt vào trang · {c.visitors} người</p>
                    <p className="text-[11px] font-medium text-success">Hôm nay +{c.todayViews}</p>
                  </div>
                  <div>
                    <p className="text-2xl font-semibold tabular-nums text-fg">{c.clicks}</p>
                    <p className="text-[11px] text-fg-muted">Bấm “Mở Facebook” · {rate(c.clicks, c.views)}</p>
                    <p className="text-[11px] font-medium text-success">Hôm nay +{c.todayClicks}</p>
                  </div>
                </div>
                <div className="mt-1.5 flex flex-wrap items-center justify-between gap-1 border-t border-line pt-1.5 text-[11px] text-fg-subtle">
                  <span>Tính trên trang tuongtaccheo.xyz trong thời gian chạy chiến dịch</span>
                  <a href={href('/cheo-lai')} className="font-medium text-accent">Xem giờ từng lượt bấm →</a>
                </div>
              </div>
              <div className="grid grid-cols-3 gap-2 text-center">
                {[{ l: 'Nhóm mục tiêu', v: c.groupIds.length }, { l: 'Page phụ', v: c.pageIds.length }, { l: 'Đã chia sẻ', v: `${c.activitiesDone}/${c.activitiesPlanned}` }].map((x) => (
                  <div key={x.l} className="rounded-lg bg-subtle py-2">
                    <p className="text-lg font-semibold tabular-nums text-fg">{x.v}</p>
                    <p className="text-[11px] text-fg-muted">{x.l}</p>
                  </div>
                ))}
              </div>
              <div>
                <div className="mb-1 flex justify-between text-xs text-fg-muted">
                  <span>Đã chia sẻ vào nhóm</span>
                  <span>{c.activitiesDone}/{c.activitiesPlanned}</span>
                </div>
                <ProgressBar value={c.activitiesPlanned ? (100 * c.activitiesDone) / c.activitiesPlanned : 0} tone="success" />
              </div>
              <div className="flex flex-wrap gap-1.5 text-xs text-fg-muted">
                {c.pageIds.map((id) => <Badge key={id} tone="info">{pageName[id] ?? 'Page'}</Badge>)}
                <Badge>{c.groupIds.length} Group</Badge>
                {(c.startDate || c.endDate) && <span className="inline-flex items-center gap-1"><CalendarRange className="h-3 w-3" />{fmtDate(c.startDate)} → {fmtDate(c.endDate)}</span>}
                {c.owner && <span>· {c.owner}</span>}
              </div>
              {c.resultNote && <p className="line-clamp-2 text-xs text-fg-muted">📝 {c.resultNote}</p>}
              <div className="flex flex-wrap gap-2">
                <Button size="sm" variant="primary" icon={<ListChecks className="h-3.5 w-3.5" />} onClick={() => setLinksOf(c)}>Tiến độ chia sẻ</Button>
                <Button size="sm" variant="secondary" icon={<Pencil className="h-3.5 w-3.5" />} onClick={() => setEditing(c)}>Sửa</Button>
                <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} onClick={() => remove(c)}>Xoá</Button>
              </div>
            </Card>
          ))}
        </div>
      )}

      <CampaignForm open={adding || editing != null} initial={editing} pages={pages ?? []} groups={groups ?? []}
        onClose={() => { setAdding(false); setEditing(null); }}
        onSaved={(c) => setData((l) => (l?.some((x) => x.id === c.id) ? l.map((x) => (x.id === c.id ? c : x)) : [c, ...(l ?? [])]))} />
      <ProgressDrawer campaign={linksOf} onClose={() => { setLinksOf(null); reload(); }} />
    </div>
  );
}
