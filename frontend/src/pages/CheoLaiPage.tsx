import { useEffect, useMemo, useState } from 'react';
import { Check, ExternalLink, HeartHandshake, Monitor, MousePointerClick, RefreshCw, Smartphone, Undo2 } from 'lucide-react';
import { Badge, Button, Card, EmptyState, PageHeader, Skeleton, Tabs } from '../components/ui';
import { useToast } from '../components/feedback';
import { api, LandingClickDto, LandingStatsDto } from '../services/api';
import { timeAgo } from '../lib/format';

type Tab = 'waiting' | 'done' | 'all';

/** The public page and the Facebook Page it promotes (same as frontend/src/landing/cheoUyTinh.config.ts). */
const SLUG = 'cheo-uy-tinh';
const PUBLIC_URL = '/cheo-uy-tinh';
const PAGE_URL = 'https://www.facebook.com/1358397297367678';

function dayLabel(d: Date) {
  const today = new Date();
  const yesterday = new Date();
  yesterday.setDate(today.getDate() - 1);
  if (d.toDateString() === today.toDateString()) return 'Hôm nay';
  if (d.toDateString() === yesterday.toDateString()) return 'Hôm qua';
  return d.toLocaleDateString('vi-VN', { weekday: 'long', day: 'numeric', month: 'numeric', year: 'numeric' });
}

/**
 * "Chéo lại": every click on "Mở Facebook – ủng hộ Page ngay" on the public page /cheo-uy-tinh, with the exact time.
 * Facebook never tells a website who clicked, so the owner matches the time with the Page's
 * "… started following your Page" notifications, interacts back, then ticks "Đã trả lại".
 */
export function CheoLaiPage() {
  const toast = useToast();
  const [tab, setTab] = useState<Tab>('waiting');
  const [clicks, setClicks] = useState<LandingClickDto[] | null>(null);
  const [stats, setStats] = useState<LandingStatsDto | null>(null);
  const [todayClicks, setTodayClicks] = useState<number | null>(null);
  const [waitingCount, setWaitingCount] = useState<number | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = async (t: Tab = tab, quiet = false) => {
    try {
      const [list, waiting, all, s] = await Promise.all([
        api.getLandingClicks(SLUG, t),
        api.getLandingClicks(SLUG, 'waiting'),
        api.getLandingClicks(SLUG, 'all', 1),
        api.getLandingStats(SLUG),
      ]);
      setClicks(list);
      setWaitingCount(waiting.length);
      const today = new Date().toDateString();
      setTodayClicks(all.filter((c) => new Date(c.clickedAtUtc).toDateString() === today).length);
      setStats(s);
    } catch (e) {
      if (!quiet) toast.error('Không tải được lượt bấm', (e as Error).message);
    }
  };

  useEffect(() => {
    document.title = 'Chéo lại · Facebook chéo uy tính';
  }, []);
  useEffect(() => {
    setClicks(null);
    load(tab);
    const t = setInterval(() => load(tab, true), 30000);
    return () => clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab]);

  const setHandled = async (c: LandingClickDto, handled: boolean) => {
    setBusy(c.id);
    try {
      await api.setLandingClickHandled(c.id, handled);
      setClicks((list) =>
        (list ?? [])
          .map((x) => (x.id === c.id ? { ...x, handledAtUtc: handled ? new Date().toISOString() : null } : x))
          .filter((x) => tab === 'all' || (tab === 'waiting' ? !x.handledAtUtc : !!x.handledAtUtc))
      );
      setWaitingCount((n) => (n == null ? n : Math.max(0, n + (handled ? -1 : 1))));
    } catch (e) {
      toast.error('Chưa lưu được', (e as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const groups = useMemo(() => {
    const map = new Map<string, { label: string; items: LandingClickDto[] }>();
    for (const c of clicks ?? []) {
      const d = new Date(c.clickedAtUtc);
      const key = d.toDateString();
      if (!map.has(key)) map.set(key, { label: dayLabel(d), items: [] });
      map.get(key)!.items.push(c);
    }
    return [...map.values()];
  }, [clicks]);

  const tiles = [
    { label: 'Lượt bấm hôm nay', value: todayClicks },
    { label: 'Chờ trả lại', value: waitingCount, highlight: true },
    { label: 'Lượt bấm 30 ngày', value: stats?.openPageClicks },
    { label: 'Lượt xem trang 30 ngày', value: stats?.views },
  ];

  return (
    <div className="space-y-4 pb-16">
      <PageHeader
        title="Chéo lại"
        description={
          <>
            Mỗi dòng là một lần có người bấm <b>“Mở Facebook – Ủng hộ Page ngay”</b> trên trang <code>/cheo-uy-tinh</code>.
            Facebook không cho biết người bấm là ai — so <b>giờ bấm</b> với thông báo “… đã theo dõi Trang của bạn” trên
            Facebook để biết ai, chéo lại cho họ rồi bấm <b>Đã trả lại</b>. Danh sách tự cập nhật mỗi 30 giây.
          </>
        }
        actions={
          <div className="flex flex-wrap gap-2">
            <Button size="sm" variant="secondary" icon={<RefreshCw className="h-3.5 w-3.5" />} onClick={() => load()}>Làm mới</Button>
            <a href={PAGE_URL} target="_blank" rel="noreferrer">
              <Button size="sm" variant="secondary" icon={<ExternalLink className="h-3.5 w-3.5" />}>Mở Page Facebook</Button>
            </a>
            <a href={PUBLIC_URL} target="_blank" rel="noreferrer">
              <Button size="sm" variant="ghost" icon={<ExternalLink className="h-3.5 w-3.5" />}>Xem trang public</Button>
            </a>
          </div>
        }
      />

      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        {tiles.map((t) => (
          <Card key={t.label} className={`p-3 ${t.highlight && (t.value ?? 0) > 0 ? 'border-warning/40 bg-warning/5' : ''}`}>
            <p className="text-xs text-fg-muted">{t.label}</p>
            <p className="mt-1 text-2xl font-semibold tabular-nums text-fg">{t.value ?? '—'}</p>
          </Card>
        ))}
      </div>

      <Tabs<Tab>
        items={[
          { id: 'waiting', label: 'Chờ trả lại', count: waitingCount ?? undefined },
          { id: 'done', label: 'Đã trả lại' },
          { id: 'all', label: 'Tất cả' },
        ]}
        value={tab}
        onChange={setTab}
      />

      {!clicks ? (
        <Skeleton className="h-40 w-full" />
      ) : clicks.length === 0 ? (
        <EmptyState
          icon={<HeartHandshake />}
          title={tab === 'waiting' ? 'Không còn ai chờ trả lại' : 'Chưa có lượt bấm nào'}
          description={tab === 'waiting' ? 'Khi có người bấm nút Mở Facebook trên trang public, giờ bấm sẽ hiện ở đây.' : ''}
        />
      ) : (
        <div className="space-y-4">
          {groups.map((g) => (
            <section key={g.label}>
              <h2 className="mb-1.5 px-1 text-xs font-semibold uppercase tracking-wide text-fg-subtle">
                {g.label} · {g.items.length} lượt
              </h2>
              <Card className="divide-y divide-line">
                {g.items.map((c) => {
                  const d = new Date(c.clickedAtUtc);
                  const fresh = Date.now() - d.getTime() < 15 * 60 * 1000;
                  return (
                    <div key={c.id} className="flex flex-wrap items-center gap-x-4 gap-y-2 px-3 py-2.5">
                      <div className="flex min-w-[150px] items-center gap-2.5">
                        <MousePointerClick className="h-4 w-4 shrink-0 text-accent" />
                        <div>
                          <p className="text-base font-semibold tabular-nums text-fg">
                            {d.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                          </p>
                          <p className="text-xs text-fg-muted">{timeAgo(c.clickedAtUtc)}</p>
                        </div>
                      </div>
                      <div className="flex flex-1 flex-wrap items-center gap-1.5 text-xs text-fg-muted">
                        {fresh && <Badge tone="info" dot pulse>Mới</Badge>}
                        <Badge>
                          {c.device === 'mobile' ? <Smartphone className="h-3 w-3" /> : <Monitor className="h-3 w-3" />}
                          {c.device === 'mobile' ? 'Điện thoại' : 'Máy tính'}
                        </Badge>
                        <Badge>{c.source ? `Từ ${c.source}` : 'Mở link trực tiếp'}</Badge>
                        {c.visitor && (
                          <span title="Mã ẩn danh của trình duyệt — cùng mã là cùng một người">
                            Người #{c.visitor}
                            {c.visitorClicks > 1 && <> · đã bấm {c.visitorClicks} lần</>}
                          </span>
                        )}
                      </div>
                      {c.handledAtUtc ? (
                        <div className="flex items-center gap-2">
                          <Badge tone="success">
                            <Check className="h-3 w-3" /> Đã trả lại {new Date(c.handledAtUtc).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}
                          </Badge>
                          <Button size="sm" variant="ghost" icon={<Undo2 className="h-3.5 w-3.5" />} loading={busy === c.id} onClick={() => setHandled(c, false)}>
                            Bỏ đánh dấu
                          </Button>
                        </div>
                      ) : (
                        <Button size="sm" variant="primary" icon={<Check className="h-3.5 w-3.5" />} loading={busy === c.id} onClick={() => setHandled(c, true)}>
                          Đã trả lại
                        </Button>
                      )}
                    </div>
                  );
                })}
              </Card>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
