import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { Cpu, Sparkles } from 'lucide-react';
import { Badge, Tabs, cn } from '../../components/ui';
import { StudioUnlock } from '../../components/studio/StudioUnlock';
import { useToast } from '../../components/feedback';
import { navigate } from '../../lib/router';
import { StudioError, studio } from '../../services/studio';
import { reup, ReupOverview } from '../../services/reup';
import { ReupOverviewTab } from './ReupOverviewTab';
import { ReupJobEditor, ReupNewJob } from './ReupJobEditor';
import { ReupCharactersTab } from './ReupCharactersTab';
import { ReupBackdropsTab } from './ReupBackdropsTab';
import { ReupHistoryTab } from './ReupHistoryTab';
import { ReupSettingsTab } from './ReupSettingsTab';

type Tab = 'overview' | 'new' | 'characters' | 'backdrops' | 'history' | 'settings';

const TABS: { id: Tab; label: string }[] = [
  { id: 'overview', label: 'Tổng quan' },
  { id: 'new', label: 'Tạo video' },
  { id: 'characters', label: 'Nhân vật AI' },
  { id: 'backdrops', label: 'Thư viện cảnh' },
  { id: 'history', label: 'Lịch sử render' },
  { id: 'settings', label: 'Cài đặt' },
];

interface ReupCtx {
  overview: ReupOverview | null;
  refreshOverview: () => Promise<void>;
  /** Shows an error (or the unlock box when the studio key is missing). */
  fail: (e: unknown, fallback?: string) => void;
}

const Ctx = createContext<ReupCtx | null>(null);

export function useReup() {
  const c = useContext(Ctx);
  if (!c) throw new Error('useReup outside ReupPage');
  return c;
}

/** "Reup Video": face swap / new background / audio on the owner's PC — free, no paid API, no cloud GPU. */
export function ReupPage({ segments }: { segments: string[] }) {
  const [first, second] = segments;
  const toast = useToast();
  const [needsKey, setNeedsKey] = useState(false);
  const [overview, setOverview] = useState<ReupOverview | null>(null);

  const fail = useCallback(
    (e: unknown, fallback = 'Có lỗi xảy ra.') => {
      if (e instanceof StudioError && e.needsKey) setNeedsKey(true);
      else toast.error(e instanceof Error && e.message ? e.message : fallback);
    },
    [toast]
  );

  const refreshOverview = useCallback(async () => {
    try {
      setOverview(await reup.overview());
    } catch (e) {
      fail(e, 'Không tải được tổng quan.');
    }
  }, [fail]);

  useEffect(() => {
    studio.checkAccess().then(() => setNeedsKey(false)).catch((e) => setNeedsKey(e instanceof StudioError && e.needsKey));
  }, []);

  useEffect(() => {
    refreshOverview();
    const t = setInterval(refreshOverview, 10000);
    return () => clearInterval(t);
  }, [refreshOverview]);

  const tab: Tab = first === 'jobs' ? 'new' : (TABS.find((t) => t.id === first)?.id ?? 'overview');
  useEffect(() => {
    document.title = `${TABS.find((t) => t.id === tab)?.label ?? 'Reup Video'} · Reup Video`;
  }, [tab]);

  let body;
  if (first === 'jobs' && second) body = <ReupJobEditor key={second} jobId={second} />;
  else if (tab === 'new') body = <ReupNewJob />;
  else if (tab === 'characters') body = <ReupCharactersTab />;
  else if (tab === 'backdrops') body = <ReupBackdropsTab />;
  else if (tab === 'history') body = <ReupHistoryTab />;
  else if (tab === 'settings') body = <ReupSettingsTab />;
  else body = <ReupOverviewTab />;

  return (
    <Ctx.Provider value={{ overview, refreshOverview, fail }}>
      <div className="space-y-5">
        <ReupHero overview={overview} />
        {needsKey && <StudioUnlock onUnlocked={() => setNeedsKey(false)} />}
        <Tabs items={TABS} value={tab} onChange={(t) => navigate(t === 'overview' ? '/reup' : `/reup/${t}`)} />
        {body}
      </div>
    </Ctx.Provider>
  );
}

function ReupHero({ overview }: { overview: ReupOverview | null }) {
  const online = overview?.pcOnline;
  const ready = overview?.engine.ready;
  return (
    <div className="relative overflow-hidden rounded-xl border border-line bg-surface">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(120%_140%_at_0%_0%,rgba(99,102,241,0.16),transparent_55%),radial-gradient(90%_120%_at_100%_0%,rgba(168,85,247,0.14),transparent_60%),radial-gradient(80%_100%_at_60%_120%,rgba(14,165,233,0.10),transparent_60%)]" />
      <div className="relative flex flex-col gap-4 px-5 py-5 sm:flex-row sm:items-center sm:justify-between sm:px-6">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-indigo-500 via-violet-500 to-fuchsia-500 text-white shadow-[0_4px_16px_-4px_rgba(139,92,246,0.6)]">
              <Sparkles className="h-4 w-4" />
            </span>
            <h1 className="bg-gradient-to-r from-fg to-fg-muted bg-clip-text text-xl font-semibold tracking-tight text-transparent sm:text-[22px]">
              Reup Video
            </h1>
            <Badge tone="success">0 đ · chạy trên máy</Badge>
          </div>
          <p className="mt-1.5 max-w-2xl text-[13px] text-fg-muted">
            Thay cảnh nền, thay khuôn mặt bằng nhân vật AI và xử lý âm thanh ngay trên máy tính của bạn — không API trả phí, không thuê GPU.
            Chỉ dùng video bạn có quyền chỉnh sửa và người trong video đã đồng ý.
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2 rounded-lg border border-line bg-surface/70 px-3 py-2 backdrop-blur">
          <Cpu className={cn('h-4 w-4', online ? 'text-success' : 'text-fg-subtle')} />
          <div className="text-xs leading-tight">
            <div className="font-medium text-fg">{overview == null ? 'Đang kiểm tra…' : online ? `Máy tính đang bật${overview.pcMachine ? ` (${overview.pcMachine})` : ''}` : 'Máy tính đang tắt'}</div>
            <div className="text-fg-muted">
              {overview == null ? '' : !online ? 'Video sẽ chờ tới khi máy bật' : ready ? 'Engine sẵn sàng' : 'Engine chưa cài / lỗi'}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
