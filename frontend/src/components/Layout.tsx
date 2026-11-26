import React, { useEffect, useState } from 'react';
import {
  LayoutGrid,
  Flame,
  CalendarClock,
  Film,
  Clapperboard,
  Sparkles,
  Newspaper,
  History,
  Settings,
  Plus,
  Menu as MenuIcon,
  X,
  Sun,
  Moon,
  Monitor,
  MessageCircle,
  Wand2,
  TrendingUp,
  CalendarDays,
  PenTool,
  Target,
  LineChart,
  Swords,
  SlidersHorizontal,
} from 'lucide-react';
import { cn, Dot } from './ui';
import { href } from '../lib/router';
import { useTheme, ThemePref } from '../lib/theme';
import { WorkerBadge } from './WorkerBadge';

interface NavItem {
  id: string;
  path: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  badge?: number;
}

function Logo() {
  return (
    <a href={href('/')} className="flex items-center gap-2.5 px-1">
      <span className="flex h-7 w-7 items-center justify-center rounded-md bg-brand">
        <svg viewBox="0 0 16 16" className="h-3.5 w-3.5 fill-white" aria-hidden>
          <path d="M5 3.2v9.6L12.8 8z" />
        </svg>
      </span>
      <span className="text-[15px] font-semibold tracking-tight text-fg">Trạm Tin 24/7</span>
    </a>
  );
}

function ThemeSwitch() {
  const [pref, setPref] = useTheme();
  const opts: { v: ThemePref; icon: React.ReactNode; label: string }[] = [
    { v: 'light', icon: <Sun className="h-3.5 w-3.5" />, label: 'Sáng' },
    { v: 'dark', icon: <Moon className="h-3.5 w-3.5" />, label: 'Tối' },
    { v: 'system', icon: <Monitor className="h-3.5 w-3.5" />, label: 'Theo hệ thống' },
  ];
  return (
    <div className="inline-flex rounded-md border border-line bg-muted p-0.5">
      {opts.map((o) => (
        <button
          key={o.v}
          type="button"
          title={o.label}
          aria-label={o.label}
          onClick={() => setPref(o.v)}
          className={cn(
            'flex h-6 w-7 items-center justify-center rounded-[4px] transition-colors',
            pref === o.v ? 'bg-surface text-fg shadow-card' : 'text-fg-subtle hover:text-fg'
          )}
        >
          {o.icon}
        </button>
      ))}
    </div>
  );
}

function SidebarContent({
  active,
  apiHealthy,
  awaitingCount,
  onNavigate,
}: {
  active: string;
  apiHealthy: boolean | null;
  awaitingCount: number;
  onNavigate?: () => void;
}) {
  const main: NavItem[] = [
    { id: 'dashboard', path: '/', label: 'Tổng quan', icon: LayoutGrid },
    { id: 'trending', path: '/trending', label: 'Tin hot', icon: Flame },
    { id: 'auto-post', path: '/auto-post', label: 'Tự động đăng', icon: CalendarClock },
    { id: 'videos', path: '/videos', label: 'Video', icon: Film, badge: awaitingCount || undefined },
    { id: 'articles', path: '/articles', label: 'Bài báo', icon: Newspaper },
    { id: 'activity', path: '/activity', label: 'Hoạt động', icon: History },
  ];
  const studio: NavItem[] = [
    { id: 'drama-scripts', path: '/drama-scripts', label: 'Lên kịch bản', icon: Sparkles },
    { id: 'studio', path: '/studio', label: 'Studio Video AI', icon: Clapperboard },
    { id: 'comments', path: '/comments', label: 'Bình luận', icon: MessageCircle },
  ];
  const local: NavItem[] = [{ id: 'ai-studio', path: '/ai-studio', label: 'AI Video Studio', icon: Wand2 }];
  const growthNav: NavItem[] = [
    { id: 'growth:overview', path: '/growth/overview', label: 'Tổng quan chiến dịch', icon: TrendingUp },
    { id: 'growth:calendar', path: '/growth/calendar', label: 'Lịch đăng nội dung', icon: CalendarDays },
    { id: 'growth:studio', path: '/growth/studio', label: 'Studio nội dung', icon: PenTool },
    { id: 'growth:campaigns', path: '/growth/campaigns', label: 'Quản lý chiến dịch', icon: Target },
    { id: 'growth:analytics', path: '/growth/analytics', label: 'Phân tích tăng trưởng', icon: LineChart },
    { id: 'growth:competitors', path: '/growth/competitors', label: 'Phân tích đối thủ', icon: Swords },
    { id: 'growth:settings', path: '/growth/settings', label: 'Cài đặt chiến dịch', icon: SlidersHorizontal },
  ];
  const secondary: NavItem[] = [{ id: 'settings', path: '/settings', label: 'Cài đặt', icon: Settings }];

  const renderItem = (item: NavItem) => {
    const Icon = item.icon;
    const isActive = active === item.id;
    return (
      <a
        key={item.id}
        href={href(item.path)}
        onClick={onNavigate}
        className={cn(
          'flex h-8 items-center gap-2.5 rounded-md px-2.5 text-sm transition-colors',
          isActive ? 'bg-muted font-medium text-fg' : 'text-fg-muted hover:bg-muted/70 hover:text-fg'
        )}
      >
        <Icon className={cn('h-4 w-4', isActive ? 'text-fg' : 'text-fg-subtle')} />
        <span className="flex-1">{item.label}</span>
        {item.badge !== undefined && (
          <span
            className="rounded-full bg-warning/15 px-1.5 text-xs font-medium tabular text-warning"
            title={`${item.badge} video chờ duyệt`}
          >
            {item.badge}
          </span>
        )}
      </a>
    );
  };

  return (
    <div className="flex h-full flex-col">
      <div className="px-4 pb-4 pt-5">
        <Logo />
      </div>

      <div className="px-3">
        <a
          href={href('/create')}
          onClick={onNavigate}
          className={cn(
            'flex h-9 w-full items-center justify-center gap-2 rounded-md text-sm font-medium transition-colors',
            active === 'create' ? 'bg-fg/90 text-canvas' : 'bg-fg text-canvas hover:bg-fg/90'
          )}
        >
          <Plus className="h-4 w-4" />
          Tạo video
        </a>
      </div>

      <nav className="mt-5 flex-1 space-y-0.5 overflow-y-auto px-3">
        {main.map(renderItem)}
        <div className="!mt-5 mb-1.5 px-2.5 text-xs font-semibold uppercase tracking-wider text-indigo-400">
          Video AI (Seedance)
        </div>
        {studio.map(renderItem)}
        <div className="!mt-5 mb-1.5 px-2.5 text-xs font-semibold uppercase tracking-wider text-violet-400">
          Video từ video tham chiếu
        </div>
        {local.map(renderItem)}
        <div className="!mt-5 mb-1.5 flex items-center gap-1.5 px-2.5 text-xs font-semibold uppercase tracking-wider text-emerald-500">
          <TrendingUp className="h-3.5 w-3.5" /> Chiến dịch tăng trưởng
        </div>
        {growthNav.map(renderItem)}
        <div className="!mt-5 mb-1.5 px-2.5 text-xs font-medium text-fg-subtle">Hệ thống</div>
        {secondary.map(renderItem)}
      </nav>

      <div className="border-t border-line px-4 py-3">
        <div className="mb-2">
          <WorkerBadge />
        </div>
        <div className="flex items-center justify-between gap-2">
          <div className="flex min-w-0 items-center gap-2 text-xs text-fg-muted">
            <Dot tone={apiHealthy === null ? 'neutral' : apiHealthy ? 'success' : 'danger'} />
            <span className="truncate">
              {apiHealthy === null ? 'Đang kết nối…' : apiHealthy ? 'Đã kết nối' : 'Mất kết nối'}
            </span>
          </div>
          <ThemeSwitch />
        </div>
      </div>
    </div>
  );
}

export function Layout({
  active,
  apiHealthy,
  awaitingCount,
  simulation,
  children,
}: {
  active: string;
  apiHealthy: boolean | null;
  awaitingCount: number;
  simulation?: boolean;
  children: React.ReactNode;
}) {
  const [mobileOpen, setMobileOpen] = useState(false);

  useEffect(() => setMobileOpen(false), [active]);

  return (
    <div className="min-h-screen">
      {/* Desktop sidebar */}
      <aside className="fixed inset-y-0 left-0 z-20 hidden w-60 border-r border-line bg-subtle lg:block">
        <SidebarContent active={active} apiHealthy={apiHealthy} awaitingCount={awaitingCount} />
      </aside>

      {/* Mobile top bar — slim: brand + connection status + menu button */}
      <header className="pt-safe sticky top-0 z-20 border-b border-line bg-surface/90 backdrop-blur lg:hidden">
        <div className="flex h-12 items-center justify-between px-3 sm:px-4">
          <Logo />
          <div className="flex items-center gap-2">
            <WorkerBadge compact />
            <span className="flex items-center gap-1.5 text-xs text-fg-muted">
              <Dot tone={apiHealthy === null ? 'neutral' : apiHealthy ? 'success' : 'danger'} />
              <span className="hidden sm:inline">
                {apiHealthy === null ? 'Đang kết nối' : apiHealthy ? 'Đã kết nối' : 'Mất kết nối'}
              </span>
            </span>
            <button
              type="button"
              onClick={() => setMobileOpen(true)}
              className="flex h-8 w-8 items-center justify-center rounded-md border border-line bg-muted/60 text-fg transition-colors hover:bg-muted"
              aria-label="Mở menu điều hướng"
            >
              <MenuIcon className="h-4 w-4" />
            </button>
          </div>
        </div>
      </header>

      {/* Mobile bottom tab bar (thumb-reachable) */}
      <nav className="pb-safe fixed inset-x-0 bottom-0 z-30 border-t border-line bg-surface/95 backdrop-blur lg:hidden">
        <div className="mx-auto grid h-16 max-w-md grid-cols-5 items-center">
          <TabLink to="/" label="Tổng quan" icon={LayoutGrid} active={active === 'dashboard'} />
          <TabLink to="/trending" label="Tin hot" icon={Flame} active={active === 'trending'} />
          <a href={href('/create')} className="flex flex-col items-center justify-center" aria-label="Tạo video">
            <span
              className={cn(
                'flex h-11 w-11 items-center justify-center rounded-full shadow-pop transition-transform active:scale-95',
                active === 'create' ? 'bg-fg/85 text-canvas' : 'bg-fg text-canvas'
              )}
            >
              <Plus className="h-5 w-5" />
            </span>
          </a>
          <TabLink to="/videos" label="Video" icon={Film} active={active === 'videos'} badge={awaitingCount || undefined} />
          <button
            type="button"
            onClick={() => setMobileOpen(true)}
            className={cn(
              'flex h-full flex-col items-center justify-center gap-0.5 text-[11px] transition-colors',
              (['auto-post', 'articles', 'activity', 'settings', 'studio'].includes(active) || active.startsWith('growth:')) ? 'font-medium text-fg' : 'text-fg-muted'
            )}
          >
            <MenuIcon className="h-5 w-5" />
            Thêm
          </button>
        </div>
      </nav>

      {/* Mobile drawer */}
      {mobileOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 animate-fade-in bg-black/40" onClick={() => setMobileOpen(false)} />
          <aside className="pt-safe pb-safe absolute inset-y-0 left-0 w-72 max-w-[85vw] animate-slide-in-left border-r border-line bg-subtle">
            <button
              type="button"
              onClick={() => setMobileOpen(false)}
              className="absolute right-3 top-4 z-10 flex h-8 w-8 items-center justify-center rounded-md text-fg-muted hover:bg-muted"
              aria-label="Đóng menu"
            >
              <X className="h-4 w-4" />
            </button>
            <SidebarContent
              active={active}
              apiHealthy={apiHealthy}
              awaitingCount={awaitingCount}
              onNavigate={() => setMobileOpen(false)}
            />
          </aside>
        </div>
      )}

      <main className="min-w-0 max-w-full overflow-x-hidden lg:pl-60">
        {simulation && (
          <div className="border-b border-warning/30 bg-warning/10 px-4 py-2 text-center text-[13px] text-warning sm:px-6">
            Máy chủ đang bật chế độ giả lập (Simulation:Enabled). Kịch bản, giọng đọc, video và bài đăng Facebook có thể là dữ liệu mẫu, không phải kết quả thật.
          </div>
        )}
        {/* Extra bottom padding on phones so content clears the tab bar */}
        <div className="mx-auto w-full max-w-[1200px] px-3 pb-28 pt-4 sm:px-6 lg:px-10 lg:py-8">{children}</div>
      </main>
    </div>
  );
}

function TabLink({
  to,
  label,
  icon: Icon,
  active,
  badge,
}: {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  active: boolean;
  badge?: number;
}) {
  return (
    <a
      href={href(to)}
      className={cn(
        'relative flex h-full flex-col items-center justify-center gap-0.5 text-[11px] transition-colors',
        active ? 'font-medium text-fg' : 'text-fg-muted'
      )}
      aria-current={active ? 'page' : undefined}
    >
      <span className="relative">
        <Icon className="h-5 w-5" />
        {badge !== undefined && (
          <span className="absolute -right-2.5 -top-1.5 min-w-[18px] rounded-full bg-warning px-1 text-center text-[10px] font-semibold leading-[18px] text-white">
            {badge > 99 ? '99+' : badge}
          </span>
        )}
      </span>
      {label}
    </a>
  );
}
