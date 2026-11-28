import { useMemo, useState } from 'react';
import { Check, Clock, ExternalLink, Info, Undo2, UserPlus, Users, X } from 'lucide-react';
import { Alert, Badge, Button, Card, EmptyState, Segmented, Skeleton, cn } from '../../../components/ui';
import { useToast } from '../../../components/feedback';
import { href } from '../../../lib/router';
import { Group, MEMBERSHIP_LABEL, MembershipStatus, outreach } from '../outreachApi';
import { PageAvatar, useLoad } from '../shared';

type Filter = 'todo' | 'requested' | 'joined' | 'all';
const compact = (n: number) => new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 }).format(n);

/**
 * Joining Groups as each helper Page, by hand. Facebook decides who joins from the profile active in the browser
 * (no API can join as a Page), so the list is per Page: switch to that Page once on Facebook, then work down its list.
 */
export function JoinSection() {
  const toast = useToast();
  const { data: pages } = useLoad(() => outreach.pages());
  const { data: groups, setData, loading } = useLoad(() => outreach.groups());
  const helpers = useMemo(() => (pages ?? []).filter((p) => p.isHelper), [pages]);
  const [pageId, setPageId] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>('todo');
  const [opened, setOpened] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState<string | null>(null);

  const active = helpers.find((p) => p.id === pageId) ?? helpers[0];
  const statusOf = (g: Group, pid: string) => g.memberships.find((m) => m.pageId === pid)?.status;

  const counts = (pid: string) => {
    const usable = (groups ?? []).filter((g) => g.fit !== 'unfit');
    return {
      total: usable.length,
      joined: usable.filter((g) => statusOf(g, pid) === 'joined').length,
      requested: usable.filter((g) => statusOf(g, pid) === 'requested').length,
    };
  };

  const list = useMemo(() => {
    if (!active) return [];
    return (groups ?? [])
      .filter((g) => g.fit !== 'unfit')
      .filter((g) => {
        const s = statusOf(g, active.id);
        return filter === 'all' || (filter === 'todo' ? !s : s === filter);
      })
      .sort((a, b) => (b.fit === 'fit' ? 1 : 0) - (a.fit === 'fit' ? 1 : 0) || (b.members ?? 0) - (a.members ?? 0));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [groups, active, filter]);

  const set = async (g: Group, status: MembershipStatus | 'none') => {
    if (!active) return;
    setBusy(g.id + status);
    try {
      const m = await outreach.setMembership(g.id, active.id, status);
      setData((all) => all?.map((x) => x.id !== g.id ? x : {
        ...x,
        memberships: status === 'none'
          ? x.memberships.filter((y) => y.pageId !== active.id)
          : [...x.memberships.filter((y) => y.pageId !== active.id), m],
      }) ?? null);
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  if (loading && !groups) return <Skeleton className="h-64 w-full" />;
  if (!helpers.length) return <EmptyState icon={<Users />} title="Chưa có Page phụ" description="Cấu hình Page phụ trong Quản lý Page phụ." />;

  return (
    <div className="space-y-4">
      <div className="grid gap-2 sm:grid-cols-3">
        {helpers.map((p) => {
          const c = counts(p.id);
          const on = active?.id === p.id;
          return (
            <button key={p.id} type="button" onClick={() => setPageId(p.id)}
              className={cn('flex items-center gap-3 rounded-xl border p-3 text-left transition-colors',
                on ? 'border-rose-500/60 bg-rose-500/[0.05] ring-1 ring-rose-500/30' : 'border-line bg-surface hover:bg-subtle')}>
              <PageAvatar url={p.pictureUrl} name={p.name} size={40} />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-semibold text-fg">{p.name}</p>
                <p className="text-xs text-fg-muted">Đã vào {c.joined} · chờ duyệt {c.requested} · còn {c.total - c.joined - c.requested}</p>
              </div>
            </button>
          );
        })}
      </div>

      {active && (
        <Alert tone="info" icon={<Info className="h-4 w-4" />}>
          Trên Facebook, <b>chuyển sang Page “{active.name}”</b> một lần (ảnh đại diện góc phải → chọn Page), rồi bấm <b>Mở Group → Tham gia</b> lần lượt
          hết danh sách bên dưới. Xong mới chọn Page tiếp theo. Facebook không cho công cụ ngoài tham gia hộ Page, nên bước bấm “Tham gia” làm trên Facebook.
        </Alert>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <Segmented<Filter> value={filter} onChange={setFilter} options={[
          { value: 'todo', label: 'Chưa xin vào' }, { value: 'requested', label: 'Chờ duyệt' }, { value: 'joined', label: 'Đã vào' }, { value: 'all', label: 'Tất cả' },
        ]} />
        <span className="text-xs text-fg-subtle">{list.length} Group</span>
        <div className="flex-1" />
        <a href={href('/outreach/finder')} className="text-xs text-accent">Tìm thêm Group →</a>
      </div>

      {list.length === 0 ? (
        <EmptyState icon={<UserPlus />} title={filter === 'todo' ? `${active?.name} đã xin vào hết các Group` : 'Không có Group nào'}
          description={filter === 'todo' ? 'Tìm thêm Group ở Group Finder, hoặc xem tab Chờ duyệt.' : ''} />
      ) : (
        <Card className="divide-y divide-line">
          {list.map((g) => {
            const s = active ? statusOf(g, active.id) : undefined;
            return (
              <div key={g.id} className={cn('flex flex-wrap items-center gap-3 px-4 py-3', opened.has(g.id) && !s && 'bg-rose-500/[0.04]')}>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className="font-medium text-fg">{g.name}</span>
                    {g.members != null && <Badge><Users className="h-3 w-3" />{compact(g.members)}</Badge>}
                    {g.privacy !== 'unknown' && <Badge tone={g.privacy === 'public' ? 'success' : 'warning'}>{g.privacy === 'public' ? 'Công khai' : 'Riêng tư'}</Badge>}
                    {g.fit === 'fit' && <Badge tone="success">Phù hợp</Badge>}
                    {s && <Badge tone={s === 'joined' ? 'success' : s === 'requested' ? 'warning' : 'danger'} dot>{MEMBERSHIP_LABEL[s]}</Badge>}
                  </div>
                  {g.topic && <p className="text-xs text-fg-subtle">{g.topic}</p>}
                </div>
                <a href={g.url} target="_blank" rel="noreferrer" onClick={() => setOpened((o) => new Set(o).add(g.id))}>
                  <Button size="sm" variant={s ? 'ghost' : 'primary'} icon={<ExternalLink className="h-3.5 w-3.5" />}>Mở Group</Button>
                </a>
                {s !== 'requested' && s !== 'joined' && (
                  <Button size="sm" variant="secondary" icon={<Clock className="h-3.5 w-3.5" />} loading={busy === g.id + 'requested'} onClick={() => set(g, 'requested')}>Đã xin vào</Button>
                )}
                {s !== 'joined' && (
                  <Button size="sm" variant="secondary" icon={<Check className="h-3.5 w-3.5" />} loading={busy === g.id + 'joined'} onClick={() => set(g, 'joined')}>Đã vào</Button>
                )}
                {s === 'requested' && (
                  <Button size="sm" variant="ghost" icon={<X className="h-3.5 w-3.5" />} loading={busy === g.id + 'rejected'} onClick={() => set(g, 'rejected')}>Bị từ chối</Button>
                )}
                {s && (
                  <Button size="sm" variant="ghost" icon={<Undo2 className="h-3.5 w-3.5" />} loading={busy === g.id + 'none'} onClick={() => set(g, 'none')} aria-label="Bỏ đánh dấu" />
                )}
              </div>
            );
          })}
        </Card>
      )}
    </div>
  );
}
