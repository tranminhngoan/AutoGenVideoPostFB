import { ArrowRight, Bot, FileText, Megaphone, Search, UsersRound } from 'lucide-react';
import { Badge, Card, CardHeader, Skeleton } from '../../../components/ui';
import { href } from '../../../lib/router';
import { outreach, STATUS_LABEL } from '../outreachApi';
import { PageAvatar, rate, Stat, useLoad } from '../shared';

const STEPS = [
  { to: '/outreach/finder', icon: Search, title: 'Tìm Group', text: 'Tìm Group phù hợp trên Facebook, thêm vào danh sách.' },
  { to: '/outreach/ai', icon: Bot, title: 'Soạn lời mời', text: 'AI viết nhiều phiên bản, lưu vào thư viện.' },
  { to: '/outreach/campaigns', icon: Megaphone, title: 'Tạo chiến dịch', text: 'Chọn Page phụ + Group, lấy link theo dõi riêng.' },
  { to: '/outreach/posts', icon: FileText, title: 'Trả lời bài Group', text: 'AI gợi ý bình luận, bạn tự đăng.' },
];

export function OverviewSection() {
  const { data: o, loading } = useLoad(() => outreach.overview());
  const { data: campaigns } = useLoad(() => outreach.campaigns());

  if (loading && !o) return <Skeleton className="h-64 w-full" />;
  if (!o) return null;
  const helpers = o.pages.filter((p) => p.isHelper);
  const running = (campaigns ?? []).filter((c) => c.status === 'Running' || c.status === 'PendingApproval');

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
        <Stat label="Lượt xem /cheo-uy-tinh · 7 ngày" value={o.views7d} hint={`${o.outreachViews7d} từ link chiến dịch`} accent />
        <Stat label="Bấm sang Facebook · 7 ngày" value={o.clicks7d} hint={`${o.outreachClicks7d} từ link chiến dịch · tỉ lệ ${rate(o.clicks7d, o.views7d)}`} accent />
        <Stat label="Group trong danh sách" value={o.groups} hint={`${o.groupsFit} nhóm phù hợp`} />
        <Stat label="Chiến dịch đang chạy" value={o.campaignsRunning} hint={`${o.campaigns} chiến dịch · ${o.postTasksTodo} bài chờ trả lời`} />
      </div>
      <p className="-mt-2 text-xs text-fg-subtle">Lượt bấm là lượt mở Facebook, không phải lượt Follow — Facebook không cho biết ai đã Follow.</p>

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        {STEPS.map((s, i) => (
          <a key={s.to} href={href(s.to)} className="group">
            <Card className="h-full p-4 transition-colors group-hover:border-rose-500/40">
              <div className="flex items-center gap-2">
                <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-rose-500/10 text-xs font-bold text-rose-500">{i + 1}</span>
                <s.icon className="h-4 w-4 text-fg-muted" />
              </div>
              <p className="mt-2 text-sm font-semibold text-fg">{s.title}</p>
              <p className="mt-0.5 text-xs text-fg-muted">{s.text}</p>
              <p className="mt-2 inline-flex items-center gap-1 text-xs font-medium text-rose-500">
                Mở <ArrowRight className="h-3 w-3 transition-transform group-hover:translate-x-0.5" />
              </p>
            </Card>
          </a>
        ))}
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader title="Page phụ đã kết nối" description="Dùng lại kết nối Facebook hiện có." actions={<a href={href('/outreach/pages')} className="text-xs text-accent">Chi tiết</a>} />
          <div className="divide-y divide-line">
            {helpers.map((p) => (
              <div key={p.id} className="flex items-center gap-3 px-4 py-2.5">
                <PageAvatar url={p.pictureUrl} name={p.name} />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-fg">{p.name}</p>
                  <p className="truncate text-xs text-fg-subtle">{p.problem ?? `ID ${p.pageId}`}</p>
                </div>
                <Badge tone={p.health === 'ok' ? 'success' : p.health === 'warning' ? 'warning' : 'danger'} dot>
                  {p.health === 'ok' ? 'Hoạt động' : p.health === 'warning' ? 'Cần chú ý' : 'Mất quyền'}
                </Badge>
              </div>
            ))}
            {helpers.length === 0 && <p className="px-4 py-3 text-sm text-fg-muted">Chưa có Page phụ nào.</p>}
          </div>
        </Card>
        <Card>
          <CardHeader title="Chiến dịch đang hoạt động" actions={<a href={href('/outreach/campaigns')} className="text-xs text-accent">Quản lý</a>} />
          <div className="divide-y divide-line">
            {running.map((c) => (
              <div key={c.id} className="flex items-center gap-3 px-4 py-2.5">
                <UsersRound className="h-4 w-4 shrink-0 text-fg-subtle" />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-fg">{c.name}</p>
                  <p className="text-xs text-fg-subtle">
                    {c.groupIds.length} Group · {c.views} lượt xem · {c.clicks} lượt bấm · đã làm {c.activitiesDone}/{c.activitiesPlanned}
                  </p>
                </div>
                <Badge tone={c.status === 'Running' ? 'success' : 'warning'}>{STATUS_LABEL[c.status]}</Badge>
              </div>
            ))}
            {running.length === 0 && <p className="px-4 py-3 text-sm text-fg-muted">Chưa có chiến dịch nào đang chạy.</p>}
          </div>
        </Card>
      </div>
    </div>
  );
}
