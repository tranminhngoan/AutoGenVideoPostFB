import { useMemo, useState } from 'react';
import { CalendarRange, Check, ExternalLink, Link2, Megaphone, Pencil, Plus, Trash2, X } from 'lucide-react';
import { Badge, Button, Card, Checkbox, Drawer, EmptyState, Field, Input, Modal, ProgressBar, Select, Skeleton, Textarea } from '../../../components/ui';
import { useConfirm, useToast } from '../../../components/feedback';
import { Activity, Campaign, CampaignInput, CampaignLink, CampaignStatus, Group, outreach, OutreachPage, STATUS_LABEL } from '../outreachApi';
import { CopyButton, fmtDate, fmtDateTime, PageAvatar, rate, useLoad } from '../shared';

const STATUS_TONE: Record<CampaignStatus, 'neutral' | 'warning' | 'success' | 'info' | 'danger'> = {
  Draft: 'neutral', PendingApproval: 'warning', Running: 'success', Paused: 'info', Completed: 'neutral',
};
const KIND_LABEL: Record<Activity['kind'], string> = { post: 'Đã đăng bài', join: 'Đã tham gia Group', comment: 'Đã bình luận' };

/** Puts the tracking link in the text: replaces any /cheo-uy-tinh link already there, else adds it at the end. */
export function withLink(text: string, link: string) {
  const t = (text ?? '').trim();
  if (!t) return link;
  return /https?:\/\/\S*cheo-uy-tinh\S*/.test(t) ? t.replace(/https?:\/\/\S*cheo-uy-tinh\S*/g, link) : `${t}\n👉 ${link}`;
}

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
      description="Đích đến luôn là trang /cheo-uy-tinh; mỗi Group × Page có link theo dõi riêng."
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

        <Field label="Nội dung quảng bá" className="sm:col-span-2" hint="Chọn từ thư viện hoặc tự viết; link theo dõi sẽ được gắn riêng cho từng Group × Page.">
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

function LinksDrawer({ campaign, onClose, onChanged }: { campaign: Campaign | null; onClose: () => void; onChanged: () => void }) {
  const toast = useToast();
  const { data: links, setData, loading } = useLoad(() => (campaign ? outreach.links(campaign.id) : Promise.resolve([] as CampaignLink[])), [campaign?.id]);
  const [mark, setMark] = useState<{ row: CampaignLink; kind: Activity['kind']; postUrl: string } | null>(null);

  const addActivity = async () => {
    if (!campaign || !mark) return;
    try {
      const a = await outreach.addActivity(campaign.id, { groupId: mark.row.groupId, pageId: mark.row.pageId, kind: mark.kind, postUrl: mark.postUrl || undefined });
      setData((list) => list?.map((r) => (r.utmContent === mark.row.utmContent ? { ...r, activities: [a, ...r.activities] } : r)) ?? null);
      setMark(null);
      onChanged();
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
    }
  };
  const removeActivity = async (row: CampaignLink, a: Activity) => {
    try {
      await outreach.deleteActivity(a.id);
      setData((list) => list?.map((r) => (r.utmContent === row.utmContent ? { ...r, activities: r.activities.filter((x) => x.id !== a.id) } : r)) ?? null);
      onChanged();
    } catch (e) {
      toast.error('Chưa xoá được', (e as Error).message);
    }
  };

  return (
    <Drawer open={campaign != null} onClose={onClose} title={campaign?.name ?? ''} subtitle={`utm_campaign=${campaign?.slug ?? ''} · làm thủ công rồi đánh dấu`}>
      {loading && !links ? <Skeleton className="h-40 w-full" /> : (links ?? []).length === 0 ? (
        <EmptyState icon={<Link2 />} title="Chiến dịch chưa có Group" description="Sửa chiến dịch để chọn Group mục tiêu." />
      ) : (
        <div className="space-y-3">
          {(links ?? []).map((row) => (
            <Card key={row.utmContent} className="space-y-2 p-3">
              <div className="flex flex-wrap items-center gap-1.5">
                <a href={row.groupUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-sm font-medium text-fg hover:text-accent">
                  {row.groupName} <ExternalLink className="h-3 w-3" />
                </a>
                {row.pageName && <Badge tone="info">qua {row.pageName}</Badge>}
                {row.pageName && (row.membership === 'joined'
                  ? <Badge tone="success">Page đã vào Group</Badge>
                  : <Badge tone="warning">{row.membership === 'requested' ? 'Page đang chờ duyệt' : row.membership === 'rejected' ? 'Page bị từ chối' : 'Page chưa vào Group'}</Badge>)}
                <span className="ml-auto text-xs text-fg-muted">{row.views} xem · {row.clicks} bấm · {rate(row.clicks, row.views)}</span>
              </div>
              <div className="flex items-center gap-2 rounded-md bg-subtle px-2 py-1.5">
                <code className="min-w-0 flex-1 truncate text-[11px] text-fg-muted">{row.link}</code>
                <CopyButton text={row.link} label="Link" />
                <CopyButton text={withLink(campaign?.contentText ?? '', row.link)} label="Nội dung + link" variant="primary" />
              </div>
              {row.activities.map((a) => (
                <div key={a.id} className="flex items-center gap-2 text-xs text-fg-muted">
                  <Check className="h-3.5 w-3.5 text-success" />
                  <span>{KIND_LABEL[a.kind]} · {fmtDateTime(a.doneAtUtc)}</span>
                  {a.postUrl && <a href={a.postUrl} target="_blank" rel="noreferrer" className="text-accent">xem bài</a>}
                  <button type="button" className="ml-auto text-fg-subtle hover:text-danger" onClick={() => removeActivity(row, a)} aria-label="Bỏ đánh dấu"><X className="h-3.5 w-3.5" /></button>
                </div>
              ))}
              {mark?.row.utmContent === row.utmContent ? (
                <div className="flex flex-wrap gap-2">
                  <Select size="sm" className="w-auto" value={mark.kind} onChange={(e) => setMark({ ...mark, kind: e.target.value as Activity['kind'] })}>
                    <option value="post">Đã đăng bài</option>
                    <option value="join">Đã tham gia Group</option>
                    <option value="comment">Đã bình luận</option>
                  </Select>
                  <div className="min-w-[180px] flex-1">
                    <Input value={mark.postUrl} onChange={(e) => setMark({ ...mark, postUrl: e.target.value })} placeholder="Link bài đã đăng (không bắt buộc)" />
                  </div>
                  <Button size="sm" variant="primary" onClick={addActivity}>Lưu</Button>
                  <Button size="sm" variant="ghost" onClick={() => setMark(null)}>Huỷ</Button>
                </div>
              ) : (
                <Button size="sm" variant="secondary" icon={<Check className="h-3.5 w-3.5" />} onClick={() => setMark({ row, kind: 'post', postUrl: '' })}>
                  Đánh dấu đã làm
                </Button>
              )}
            </Card>
          ))}
          <p className="text-xs text-fg-subtle">Lượt bấm là lượt mở Facebook từ trang /cheo-uy-tinh — không phải lượt Follow đã xác nhận.</p>
        </div>
      )}
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
        <EmptyState icon={<Megaphone />} title="Chưa có chiến dịch" description="Tạo chiến dịch: chọn Page phụ, Group mục tiêu và nội dung — hệ thống tạo link theo dõi riêng cho từng Group." />
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
              <div className="grid grid-cols-3 gap-2 text-center">
                {[{ l: 'Lượt xem', v: c.views }, { l: 'Bấm sang FB', v: c.clicks }, { l: 'Tỉ lệ', v: rate(c.clicks, c.views) }].map((x) => (
                  <div key={x.l} className="rounded-lg bg-subtle py-2">
                    <p className="text-lg font-semibold tabular-nums text-fg">{x.v}</p>
                    <p className="text-[11px] text-fg-muted">{x.l}</p>
                  </div>
                ))}
              </div>
              <div>
                <div className="mb-1 flex justify-between text-xs text-fg-muted">
                  <span>Đã làm thủ công</span>
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
                <Button size="sm" variant="primary" icon={<Link2 className="h-3.5 w-3.5" />} onClick={() => setLinksOf(c)}>Link & việc cần làm</Button>
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
      <LinksDrawer campaign={linksOf} onClose={() => setLinksOf(null)} onChanged={reload} />
    </div>
  );
}
