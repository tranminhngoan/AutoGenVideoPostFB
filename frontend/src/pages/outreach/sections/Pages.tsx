import { useState } from 'react';
import { ExternalLink, RefreshCw } from 'lucide-react';
import { Alert, Badge, Button, Card, Skeleton } from '../../../components/ui';
import { outreach } from '../outreachApi';
import { fmtDate, PageAvatar, useLoad } from '../shared';

const PERMISSION_LABEL: Record<string, string> = {
  pages_show_list: 'Xem danh sách Page',
  pages_read_engagement: 'Đọc tương tác',
  pages_manage_posts: 'Đăng bài',
  pages_manage_engagement: 'Quản lý bình luận',
  pages_read_user_content: 'Đọc nội dung người dùng',
  pages_manage_metadata: 'Quản lý cài đặt Page',
  read_insights: 'Đọc thống kê',
  instagram_basic: 'Instagram',
  instagram_content_publish: 'Đăng Instagram',
};

export function PagesSection() {
  const [refresh, setRefresh] = useState(0);
  const { data: pages, loading } = useLoad(() => outreach.pages(refresh > 0), [refresh]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-fg-muted">
          Danh sách lấy từ kết nối Facebook hiện có (Cài đặt → Facebook). Token chỉ nằm trên máy chủ; ở đây chỉ hiện kết quả kiểm tra của Meta.
        </p>
        <Button size="sm" variant="secondary" icon={<RefreshCw className="h-3.5 w-3.5" />} loading={loading} onClick={() => setRefresh((n) => n + 1)}>
          Kiểm tra lại quyền
        </Button>
      </div>
      {loading && !pages ? (
        <Skeleton className="h-48 w-full" />
      ) : (
        <div className="grid gap-3 md:grid-cols-2">
          {(pages ?? []).map((p) => (
            <Card key={p.id} className={`p-4 ${p.isMain ? 'border-rose-500/40' : p.isHelper ? 'border-accent/40' : 'opacity-60'}`}>
              <div className="flex items-start gap-3">
                <PageAvatar url={p.pictureUrl} name={p.name} size={48} />
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <p className="truncate font-semibold text-fg">{p.name}</p>
                    {p.isMain ? <Badge tone="danger">Page chính</Badge> : p.isHelper ? <Badge tone="info">Page phụ chiến dịch</Badge> : <Badge>Không dùng cho chiến dịch</Badge>}
                  </div>
                  <p className="text-xs text-fg-subtle">Page ID {p.pageId} · token hết hạn: {p.tokenExpiresAtUtc ? fmtDate(p.tokenExpiresAtUtc) : 'không hết hạn'}</p>
                </div>
                <Badge tone={p.health === 'ok' ? 'success' : p.health === 'warning' ? 'warning' : 'danger'} dot>
                  {p.health === 'ok' ? 'Hoạt động' : p.health === 'warning' ? 'Cần chú ý' : 'Mất quyền'}
                </Badge>
              </div>
              {p.problem && (
                <Alert tone={p.health === 'error' ? 'danger' : 'warning'} className="mt-3">
                  {p.problem} {p.health === 'error' && 'Vào Cài đặt → Facebook để kết nối lại. Các Page khác không bị ảnh hưởng.'}
                </Alert>
              )}
              <div className="mt-3 flex flex-wrap gap-1">
                {p.permissions.map((perm) => (
                  <span key={perm} className="rounded-md bg-muted px-1.5 py-0.5 text-[11px] text-fg-muted" title={perm}>
                    {PERMISSION_LABEL[perm] ?? perm}
                  </span>
                ))}
              </div>
              <a href={`https://www.facebook.com/${p.pageId}`} target="_blank" rel="noreferrer" className="mt-3 inline-flex items-center gap-1 text-xs text-accent">
                Mở Page <ExternalLink className="h-3 w-3" />
              </a>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
