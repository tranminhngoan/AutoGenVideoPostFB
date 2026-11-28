import { useMemo, useState } from 'react';
import { ExternalLink, Pencil, Plus, Search, ThumbsDown, ThumbsUp, Trash2, UsersRound } from 'lucide-react';
import { Badge, Button, Card, EmptyState, Field, Input, Modal, Select, Skeleton, Textarea, cn } from '../../../components/ui';
import { useConfirm, useToast } from '../../../components/feedback';
import { EXCHANGE_TYPES, Group, GroupInput, MEMBERSHIP_LABEL, outreach } from '../outreachApi';
import { rate, useLoad } from '../shared';

const empty: GroupInput = { name: '', url: '', topic: '', exchangeTypes: [], fit: 'unknown', privacy: 'unknown', rules: '', note: '' };

/** Add / edit one Group (also used by Group Finder). */
export function GroupForm({ open, initial, onClose, onSaved }: { open: boolean; initial?: Group | null; onClose: () => void; onSaved: (g: Group) => void }) {
  const toast = useToast();
  const [form, setForm] = useState<GroupInput>(empty);
  const [busy, setBusy] = useState(false);
  const [lastOpen, setLastOpen] = useState(false);
  if (open !== lastOpen) {
    setLastOpen(open);
    if (open) setForm(initial ? { ...initial, topic: initial.topic ?? '', rules: initial.rules ?? '', note: initial.note ?? '' } : empty);
  }
  const set = (patch: Partial<GroupInput>) => setForm((f) => ({ ...f, ...patch }));
  const toggleType = (t: string) =>
    set({ exchangeTypes: (form.exchangeTypes ?? []).includes(t) ? (form.exchangeTypes ?? []).filter((x) => x !== t) : [...(form.exchangeTypes ?? []), t] });

  const save = async () => {
    setBusy(true);
    try {
      const g = initial ? await outreach.editGroup(initial.id, form) : await outreach.addGroup(form);
      toast.success(initial ? 'Đã lưu Group' : 'Đã thêm Group');
      onSaved(g);
      onClose();
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title={initial ? 'Sửa Group' : 'Thêm Group'}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Huỷ</Button>
          <Button variant="primary" loading={busy} onClick={save} disabled={!form.url.trim()}>Lưu</Button>
        </>
      }
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Link Group" className="sm:col-span-2" hint="Dạng facebook.com/groups/… — link bài viết trong Group cũng được, hệ thống tự cắt lấy link Group.">
          <Input value={form.url} onChange={(e) => set({ url: e.target.value })} placeholder="https://www.facebook.com/groups/..." />
        </Field>
        <Field label="Tên Group" optional>
          <Input value={form.name} onChange={(e) => set({ name: e.target.value })} placeholder="Để trống sẽ lấy theo link" />
        </Field>
        <Field label="Chủ đề" optional>
          <Input value={form.topic ?? ''} onChange={(e) => set({ topic: e.target.value })} placeholder="hoài niệm, giải trí, công nghệ…" />
        </Field>
        <Field label="Loại trao đổi" className="sm:col-span-2">
          <div className="flex flex-wrap gap-1.5">
            {EXCHANGE_TYPES.map((t) => {
              const on = (form.exchangeTypes ?? []).includes(t.value);
              return (
                <button key={t.value} type="button" onClick={() => toggleType(t.value)}
                  className={cn('rounded-full border px-3 py-1 text-xs font-medium transition-colors',
                    on ? 'border-rose-500 bg-rose-500/10 text-rose-600' : 'border-line text-fg-muted hover:bg-muted')}>
                  {t.label}
                </button>
              );
            })}
          </div>
        </Field>
        <Field label="Đánh giá">
          <Select value={form.fit} onChange={(e) => set({ fit: e.target.value as Group['fit'] })}>
            <option value="unknown">Chưa đánh giá</option>
            <option value="fit">Phù hợp</option>
            <option value="unfit">Không phù hợp</option>
          </Select>
        </Field>
        <Field label="Quyền riêng tư">
          <Select value={form.privacy} onChange={(e) => set({ privacy: e.target.value as Group['privacy'] })}>
            <option value="unknown">Chưa rõ</option>
            <option value="public">Công khai</option>
            <option value="private">Riêng tư</option>
          </Select>
        </Field>
        <Field label="Quy định của Group" optional className="sm:col-span-2" hint="AI sẽ tuân theo khi viết nội dung cho Group này.">
          <Textarea rows={2} value={form.rules ?? ''} onChange={(e) => set({ rules: e.target.value })} placeholder="VD: không đăng link ngoài, chỉ đăng thứ 7…" />
        </Field>
        <Field label="Ghi chú" optional className="sm:col-span-2">
          <Textarea rows={2} value={form.note ?? ''} onChange={(e) => set({ note: e.target.value })} />
        </Field>
      </div>
    </Modal>
  );
}

export function FitBadge({ fit }: { fit: Group['fit'] }) {
  return fit === 'fit' ? <Badge tone="success">Phù hợp</Badge> : fit === 'unfit' ? <Badge tone="danger">Không phù hợp</Badge> : <Badge>Chưa đánh giá</Badge>;
}

type Sort = 'new' | 'name' | 'clicks';

export function GroupsSection() {
  const toast = useToast();
  const confirm = useConfirm();
  const { data: groups, setData, loading } = useLoad(() => outreach.groups());
  const { data: pages } = useLoad(() => outreach.pages());
  const helpers = (pages ?? []).filter((p) => p.isHelper);
  const [q, setQ] = useState('');
  const [fit, setFit] = useState('all');
  const [type, setType] = useState('all');
  const [topic, setTopic] = useState('all');
  const [sort, setSort] = useState<Sort>('new');
  const [editing, setEditing] = useState<Group | null>(null);
  const [adding, setAdding] = useState(false);

  const topics = useMemo(() => [...new Set((groups ?? []).map((g) => g.topic).filter(Boolean) as string[])].sort(), [groups]);
  const list = useMemo(() => {
    const term = q.trim().toLowerCase();
    return (groups ?? [])
      .filter((g) => !term || g.name.toLowerCase().includes(term) || g.url.toLowerCase().includes(term) || (g.note ?? '').toLowerCase().includes(term))
      .filter((g) => fit === 'all' || g.fit === fit)
      .filter((g) => type === 'all' || g.exchangeTypes.includes(type))
      .filter((g) => topic === 'all' || g.topic === topic)
      .sort((a, b) => (sort === 'name' ? a.name.localeCompare(b.name, 'vi') : sort === 'clicks' ? b.clicks - a.clicks : b.createdAtUtc.localeCompare(a.createdAtUtc)));
  }, [groups, q, fit, type, topic, sort]);

  const upsert = (g: Group) => setData((list) => (list?.some((x) => x.id === g.id) ? list.map((x) => (x.id === g.id ? { ...x, ...g, memberships: g.memberships ?? x.memberships } : x)) : [{ ...g, memberships: g.memberships ?? [] }, ...(list ?? [])]));

  const quickFit = async (g: Group, value: Group['fit']) => {
    try {
      upsert(await outreach.editGroup(g.id, { ...g, fit: g.fit === value ? 'unknown' : value }));
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
    }
  };

  const remove = async (g: Group) => {
    if (!(await confirm({ title: `Xoá Group "${g.name}"?`, description: 'Group sẽ bị bỏ khỏi danh sách và các chiến dịch.', confirmLabel: 'Xoá', danger: true }))) return;
    try {
      await outreach.deleteGroup(g.id);
      setData((list) => list?.filter((x) => x.id !== g.id) ?? null);
    } catch (e) {
      toast.error('Chưa xoá được', (e as Error).message);
    }
  };

  return (
    <div className="space-y-4">
      <Card className="flex flex-wrap items-center gap-2 p-3">
        <div className="min-w-[200px] flex-1">
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Tìm theo tên, link, ghi chú…" leading={<Search className="h-4 w-4" />} />
        </div>
        <Select size="sm" value={fit} onChange={(e) => setFit(e.target.value)} className="w-auto">
          <option value="all">Mọi đánh giá</option>
          <option value="fit">Phù hợp</option>
          <option value="unknown">Chưa đánh giá</option>
          <option value="unfit">Không phù hợp</option>
        </Select>
        <Select size="sm" value={type} onChange={(e) => setType(e.target.value)} className="w-auto">
          <option value="all">Mọi loại trao đổi</option>
          {EXCHANGE_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
        </Select>
        <Select size="sm" value={topic} onChange={(e) => setTopic(e.target.value)} className="w-auto">
          <option value="all">Mọi chủ đề</option>
          {topics.map((t) => <option key={t} value={t}>{t}</option>)}
        </Select>
        <Select size="sm" value={sort} onChange={(e) => setSort(e.target.value as Sort)} className="w-auto">
          <option value="new">Mới thêm</option>
          <option value="name">Tên A–Z</option>
          <option value="clicks">Nhiều lượt bấm</option>
        </Select>
        <Button size="sm" variant="primary" icon={<Plus className="h-3.5 w-3.5" />} onClick={() => setAdding(true)}>Thêm Group</Button>
      </Card>

      {loading && !groups ? (
        <Skeleton className="h-48 w-full" />
      ) : list.length === 0 ? (
        <EmptyState icon={<UsersRound />} title={groups?.length ? 'Không có Group nào khớp bộ lọc' : 'Chưa có Group nào'}
          description="Thêm bằng tay, hoặc vào Group Finder để tìm và nhập từ file CSV." />
      ) : (
        <div className="space-y-2">
          <p className="px-1 text-xs text-fg-subtle">{list.length} / {groups?.length ?? 0} Group</p>
          {list.map((g) => (
            <Card key={g.id} className="p-3.5">
              <div className="flex flex-wrap items-start gap-3">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <a href={g.url} target="_blank" rel="noreferrer" className="truncate font-medium text-fg hover:text-accent">{g.name}</a>
                    <ExternalLink className="h-3 w-3 text-fg-subtle" />
                    <FitBadge fit={g.fit} />
                    {g.privacy !== 'unknown' && <Badge>{g.privacy === 'public' ? 'Công khai' : 'Riêng tư'}</Badge>}
                    <span className="font-mono text-[11px] text-fg-subtle">#{g.key}</span>
                  </div>
                  <div className="mt-1 flex flex-wrap items-center gap-1.5 text-xs text-fg-muted">
                    {g.topic && <Badge tone="info">{g.topic}</Badge>}
                    {g.exchangeTypes.map((t) => <Badge key={t}>{EXCHANGE_TYPES.find((x) => x.value === t)?.label ?? t}</Badge>)}
                    <span>{g.views} lượt xem · {g.clicks} lượt bấm · tỉ lệ {rate(g.clicks, g.views)}</span>
                  </div>
                  {helpers.length > 0 && (
                    <div className="mt-1.5 flex flex-wrap gap-1.5 text-[11px]">
                      {helpers.map((p) => {
                        const s = g.memberships.find((m) => m.pageId === p.id)?.status;
                        return (
                          <span key={p.id} className={cn('rounded-md px-1.5 py-0.5', s === 'joined' ? 'bg-success/10 text-success' : s === 'requested' ? 'bg-warning/10 text-warning' : s === 'rejected' ? 'bg-danger/10 text-danger' : 'bg-muted text-fg-subtle')}>
                            {p.name.replace(/^Trạm Tin\s*/i, '')}: {s ? MEMBERSHIP_LABEL[s] : 'chưa vào'}
                          </span>
                        );
                      })}
                    </div>
                  )}
                  {(g.rules || g.note) && (
                    <p className="mt-1.5 line-clamp-2 text-xs text-fg-muted">
                      {g.rules && <><b>Quy định:</b> {g.rules} </>}
                      {g.note && <><b>Ghi chú:</b> {g.note}</>}
                    </p>
                  )}
                </div>
                <div className="flex items-center gap-1">
                  <Button size="sm" variant={g.fit === 'fit' ? 'primary' : 'ghost'} icon={<ThumbsUp className="h-3.5 w-3.5" />} onClick={() => quickFit(g, 'fit')} aria-label="Phù hợp" />
                  <Button size="sm" variant={g.fit === 'unfit' ? 'danger' : 'ghost'} icon={<ThumbsDown className="h-3.5 w-3.5" />} onClick={() => quickFit(g, 'unfit')} aria-label="Không phù hợp" />
                  <Button size="sm" variant="ghost" icon={<Pencil className="h-3.5 w-3.5" />} onClick={() => setEditing(g)} aria-label="Sửa" />
                  <Button size="sm" variant="danger-soft" icon={<Trash2 className="h-3.5 w-3.5" />} onClick={() => remove(g)} aria-label="Xoá" />
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      <GroupForm open={adding || editing != null} initial={editing} onClose={() => { setAdding(false); setEditing(null); }} onSaved={upsert} />
    </div>
  );
}
