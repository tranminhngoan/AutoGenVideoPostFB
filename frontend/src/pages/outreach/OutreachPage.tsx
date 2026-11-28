import { FormEvent, useEffect, useState } from 'react';
import { Lock, LogOut, Megaphone, ShieldCheck } from 'lucide-react';
import { Alert, Button, Card, Input } from '../../components/ui';
import { getSession, LOCK_EVENT, outreach } from './outreachApi';
import { OverviewSection } from './sections/Overview';
import { CampaignsSection } from './sections/Campaigns';
import { FinderSection } from './sections/Finder';
import { GroupsSection } from './sections/Groups';
import { AiSection } from './sections/AiOutreach';
import { PostsSection } from './sections/Posts';
import { PagesSection } from './sections/Pages';
import { StatsSection } from './sections/Stats';
import { SecuritySection } from './sections/Security';

const SECTIONS: Record<string, { title: string; subtitle: string }> = {
  overview: { title: 'Tổng quan chiến dịch', subtitle: 'Toàn cảnh hoạt động kéo tương tác về Page chính qua trang /cheo-uy-tinh.' },
  campaigns: { title: 'Quản lý chiến dịch', subtitle: 'Mỗi chiến dịch: Page phụ, Group mục tiêu, nội dung, link theo dõi riêng cho từng Group × Page.' },
  finder: { title: 'Group Finder', subtitle: 'Tìm Group phù hợp trên Facebook rồi thêm vào danh sách — thủ công, đúng chính sách Meta.' },
  groups: { title: 'Danh sách Group', subtitle: 'Quản lý, phân loại và lọc các Group trao đổi tương tác.' },
  ai: { title: 'AI Outreach', subtitle: 'AI viết lời mời tự nhiên theo ngữ cảnh Group — bạn duyệt, copy và tự đăng.' },
  posts: { title: 'Quản lý bài viết Group', subtitle: 'Bài viết trong Group cần trả lời — AI gợi ý bình luận có giá trị, bạn tự đăng.' },
  pages: { title: 'Quản lý Page phụ', subtitle: 'Kết nối Facebook hiện có — kiểm tra quyền, không cần nhập lại token.' },
  stats: { title: 'Thống kê hiệu quả', subtitle: 'Lượt truy cập /cheo-uy-tinh và lượt bấm sang Facebook, theo ngày, nguồn, chiến dịch, Group.' },
  security: { title: 'Cấu hình bảo mật', subtitle: 'Mật khẩu chiến dịch, thời hạn phiên và nhật ký mở khoá.' },
};

function Gate({ onUnlocked }: { onUnlocked: () => void }) {
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!password) return;
    setBusy(true);
    setError(null);
    try {
      const r = await outreach.unlock(password);
      if (r.ok) {
        setPassword('');
        onUnlocked();
      } else if (r.lockedUntilUtc) {
        setError(`${r.error ?? 'Tạm khoá.'} Mở lại lúc ${new Date(r.lockedUntilUtc).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}.`);
      } else {
        setError(`${r.error ?? 'Sai mật khẩu.'}${r.attemptsLeft != null ? ` Còn ${r.attemptsLeft} lần thử.` : ''}`);
      }
    } catch {
      setError('Không kết nối được máy chủ.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex min-h-[70vh] items-center justify-center">
      <Card className="relative w-full max-w-md overflow-hidden p-0">
        <div className="bg-gradient-to-br from-rose-500 via-fuchsia-600 to-indigo-600 px-6 py-6 text-white">
          <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-white/15 ring-1 ring-white/30 backdrop-blur">
            <ShieldCheck className="h-6 w-6" />
          </div>
          <h1 className="mt-3 text-lg font-semibold">Chiến dịch kéo tương tác</h1>
          <p className="mt-0.5 text-sm text-white/80">Khu vực riêng — nhập mật khẩu chiến dịch để tiếp tục.</p>
        </div>
        <form onSubmit={submit} className="space-y-3 p-6">
          <Input
            type="password"
            autoFocus
            autoComplete="current-password"
            placeholder="Mật khẩu chiến dịch"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            leading={<Lock className="h-4 w-4" />}
          />
          {error && <Alert tone="danger">{error}</Alert>}
          <Button type="submit" variant="primary" className="w-full" loading={busy}>
            Mở khoá
          </Button>
          <p className="text-center text-xs text-fg-subtle">Mật khẩu được kiểm tra trên máy chủ. Nhập sai nhiều lần sẽ bị tạm khoá.</p>
        </form>
      </Card>
    </div>
  );
}

/** "Chiến dịch kéo tương tác" — every section sits behind the campaign password (server-checked session). */
export function OutreachPage({ section }: { section: string }) {
  const [unlocked, setUnlocked] = useState(() => getSession() != null);
  const meta = SECTIONS[section] ?? SECTIONS.overview;

  useEffect(() => {
    document.title = `${meta.title} · Chiến dịch kéo tương tác`;
  }, [meta.title]);

  useEffect(() => {
    const onLock = () => setUnlocked(false);
    window.addEventListener(LOCK_EVENT, onLock);
    // Expiry while the page stays open
    const t = setInterval(() => {
      if (!getSession()) setUnlocked(false);
    }, 30000);
    return () => {
      window.removeEventListener(LOCK_EVENT, onLock);
      clearInterval(t);
    };
  }, []);

  if (!unlocked) return <Gate onUnlocked={() => setUnlocked(true)} />;

  const session = getSession();
  const body = (() => {
    switch (section) {
      case 'campaigns': return <CampaignsSection />;
      case 'finder': return <FinderSection />;
      case 'groups': return <GroupsSection />;
      case 'ai': return <AiSection />;
      case 'posts': return <PostsSection />;
      case 'pages': return <PagesSection />;
      case 'stats': return <StatsSection />;
      case 'security': return <SecuritySection />;
      default: return <OverviewSection />;
    }
  })();

  return (
    <div className="space-y-5 pb-16">
      <div className="relative overflow-hidden rounded-xl border border-line bg-surface">
        <div className="pointer-events-none absolute -right-16 -top-20 h-56 w-56 rounded-full bg-gradient-to-br from-rose-500/20 via-fuchsia-500/15 to-indigo-500/10 blur-2xl" />
        <div className="relative flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5">
          <div className="flex items-start gap-3">
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-rose-500 to-fuchsia-600 text-white shadow-md shadow-rose-500/25">
              <Megaphone className="h-5 w-5" />
            </span>
            <div>
              <p className="text-[11px] font-semibold uppercase tracking-wider text-rose-500">Chiến dịch kéo tương tác</p>
              <h1 className="text-xl font-semibold leading-tight text-fg">{meta.title}</h1>
              <p className="mt-0.5 text-[13px] text-fg-muted">{meta.subtitle}</p>
            </div>
          </div>
          <div className="flex items-center gap-2 text-xs text-fg-muted">
            {session && <span>Phiên đến {new Date(session.expiresAtUtc).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}</span>}
            <Button size="sm" variant="ghost" icon={<LogOut className="h-3.5 w-3.5" />} onClick={() => outreach.lock()}>
              Khoá
            </Button>
          </div>
        </div>
      </div>
      {body}
    </div>
  );
}
