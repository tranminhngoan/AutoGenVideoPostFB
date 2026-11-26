import { useCallback, useEffect, useMemo, useState } from 'react';
import { TrendingUp } from 'lucide-react';
import { ButtonLink, Select, Tabs } from '../../components/ui';
import { StudioUnlock } from '../../components/studio/StudioUnlock';
import { navigate } from '../../lib/router';
import { growth, type Campaign, type GrowthPage as Page } from '../../services/growth';
import { StudioError } from '../../services/studio';
import { CampaignBadge, LoadError, LoadingBlock, PageAvatar, type GrowthCtx } from './shared';
import { OverviewView } from './OverviewView';
import { CalendarView } from './CalendarView';
import { ContentStudioView } from './ContentStudioView';
import { CampaignsView } from './CampaignsView';
import { AnalyticsView } from './AnalyticsView';
import { CompetitorsView } from './CompetitorsView';
import { SettingsView } from './SettingsView';
import { dm } from '../../lib/growthMath';

export const GROWTH_SECTIONS = [
  { id: 'overview', label: 'Tổng quan' },
  { id: 'calendar', label: 'Lịch đăng' },
  { id: 'studio', label: 'Studio nội dung' },
  { id: 'campaigns', label: 'Quản lý chiến dịch' },
  { id: 'analytics', label: 'Phân tích tăng trưởng' },
  { id: 'competitors', label: 'Đối thủ' },
  { id: 'settings', label: 'Cài đặt' },
] as const;
export type GrowthSection = (typeof GROWTH_SECTIONS)[number]['id'];

const PAGE_KEY = 'growth.page';
const CAMPAIGN_KEY = 'growth.campaign';
const remember = (k: string, v: string | null) => {
  try {
    if (v) localStorage.setItem(k, v);
    else localStorage.removeItem(k);
  } catch {
    /* private mode */
  }
};
const recall = (k: string) => {
  try {
    return localStorage.getItem(k);
  } catch {
    return null;
  }
};

/** "Chiến dịch tăng trưởng" — the shell: Page + campaign pickers (Pages from the existing connection) and the 7 sub-pages. */
export function GrowthPage({ segments }: { segments: string[] }) {
  const section = (GROWTH_SECTIONS.find((s) => s.id === segments[0])?.id ?? 'overview') as GrowthSection;
  const [needKey, setNeedKey] = useState(false);
  const [pages, setPages] = useState<Page[] | null>(null);
  const [pagesError, setPagesError] = useState<string | null>(null);
  const [pageId, setPageId] = useState<string | null>(recall(PAGE_KEY));
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [campaignId, setCampaignId] = useState<string | null>(recall(CAMPAIGN_KEY));
  const [loadingCampaigns, setLoadingCampaigns] = useState(false);

  useEffect(() => {
    document.title = `${GROWTH_SECTIONS.find((s) => s.id === section)?.label} · Chiến dịch tăng trưởng`;
  }, [section]);

  const loadPages = useCallback(async () => {
    setPagesError(null);
    try {
      const list = await growth.pages();
      setPages(list);
      setNeedKey(false);
      setPageId((cur) => (cur && list.some((p) => p.id === cur) ? cur : list.find((p) => p.isActive)?.id ?? list[0]?.id ?? null));
    } catch (e) {
      if (e instanceof StudioError && e.needsKey) setNeedKey(true);
      else setPagesError((e as Error).message);
    }
  }, []);

  useEffect(() => {
    loadPages();
  }, [loadPages]);

  const reloadCampaigns = useCallback(async () => {
    if (!pageId) {
      setCampaigns([]);
      return;
    }
    setLoadingCampaigns(true);
    try {
      const list = await growth.campaigns(pageId);
      setCampaigns(list);
      setCampaignId((cur) => {
        if (cur && list.some((c) => c.id === cur)) return cur;
        const live = list.find((c) => c.status === 'Running' || c.status === 'Paused') ?? list.find((c) => c.status === 'Draft') ?? list[0];
        return live?.id ?? null;
      });
    } catch (e) {
      if (e instanceof StudioError && e.needsKey) setNeedKey(true);
      else setPagesError((e as Error).message);
    } finally {
      setLoadingCampaigns(false);
    }
  }, [pageId]);

  useEffect(() => {
    remember(PAGE_KEY, pageId);
    reloadCampaigns();
  }, [pageId, reloadCampaigns]);

  useEffect(() => remember(CAMPAIGN_KEY, campaignId), [campaignId]);

  const page = useMemo(() => pages?.find((p) => p.id === pageId) ?? null, [pages, pageId]);
  const campaign = useMemo(() => campaigns.find((c) => c.id === campaignId) ?? null, [campaigns, campaignId]);

  const ctx: GrowthCtx = {
    pages: pages ?? [],
    page,
    campaigns,
    campaign,
    selectPage: (id) => {
      setPageId(id);
      setCampaignId(null);
    },
    selectCampaign: setCampaignId,
    reloadCampaigns,
    setCampaign: (c) => setCampaigns((list) => (list.some((x) => x.id === c.id) ? list.map((x) => (x.id === c.id ? c : x)) : [c, ...list])),
    onNeedKey: () => setNeedKey(true),
  };

  if (needKey)
    return (
      <div className="mx-auto max-w-lg space-y-4">
        <Header />
        <StudioUnlock onUnlocked={() => { setNeedKey(false); loadPages(); }} />
      </div>
    );

  return (
    <div className="space-y-5">
      <Header />

      {/* Page + campaign pickers */}
      <div className="flex flex-col gap-3 rounded-lg border border-line bg-surface p-3 shadow-card sm:flex-row sm:items-center">
        {pages === null && !pagesError ? (
          <div className="h-9 w-full animate-pulse rounded-md bg-muted" />
        ) : pages && pages.length === 0 ? (
          <div className="flex flex-1 flex-wrap items-center justify-between gap-3 text-[13px] text-fg-muted">
            Chưa có Page nào được kết nối.
            <ButtonLink href="#/settings/facebook" size="sm" variant="secondary">Kết nối Page ở Cài đặt</ButtonLink>
          </div>
        ) : (
          <>
            <div className="flex min-w-0 flex-1 items-center gap-3">
              <PageAvatar page={page} size={36} />
              <Select aria-label="Chọn Page" className="min-w-0 flex-1" value={pageId ?? ''} onChange={(e) => ctx.selectPage(e.target.value)}>
                {(pages ?? []).map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.pageName}
                    {!p.isActive || !p.hasToken ? ' (đã ngắt kết nối)' : ''}
                  </option>
                ))}
              </Select>
            </div>
            <div className="flex min-w-0 flex-1 items-center gap-2">
              <Select aria-label="Chọn chiến dịch" className="min-w-0 flex-1" value={campaignId ?? ''} onChange={(e) => setCampaignId(e.target.value || null)} disabled={loadingCampaigns || campaigns.length === 0}>
                {campaigns.length === 0 && <option value="">{loadingCampaigns ? 'Đang tải…' : 'Chưa có chiến dịch'}</option>}
                {campaigns.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name} · {dm(c.startDate)}–{dm(c.endDate)}
                  </option>
                ))}
              </Select>
              {campaign && <CampaignBadge status={campaign.status} />}
            </div>
          </>
        )}
      </div>

      {pagesError && <LoadError message={pagesError} onRetry={loadPages} />}

      <Tabs
        items={GROWTH_SECTIONS.map((s) => ({ id: s.id, label: s.label }))}
        value={section}
        onChange={(id) => navigate(`/growth/${id}`)}
      />

      {pages === null && !pagesError ? (
        <LoadingBlock />
      ) : (
        <div key={`${section}:${campaign?.id ?? 'none'}`}>
          {section === 'overview' && <OverviewView ctx={ctx} />}
          {section === 'calendar' && <CalendarView ctx={ctx} />}
          {section === 'studio' && <ContentStudioView ctx={ctx} />}
          {section === 'campaigns' && <CampaignsView ctx={ctx} />}
          {section === 'analytics' && <AnalyticsView ctx={ctx} />}
          {section === 'competitors' && <CompetitorsView ctx={ctx} />}
          {section === 'settings' && <SettingsView ctx={ctx} />}
        </div>
      )}
    </div>
  );
}

function Header() {
  return (
    <div className="flex items-start gap-3">
      <span className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-accent/90 to-accent/60 text-accent-fg shadow-card">
        <TrendingUp className="h-[18px] w-[18px]" />
      </span>
      <div className="min-w-0">
        <h1 className="text-xl font-semibold leading-tight tracking-tight text-fg sm:text-[22px]">Chiến dịch tăng trưởng</h1>
        <p className="mt-0.5 text-[13px] text-fg-muted">Tăng follower trong 7 ngày dùng thử Meta One — số liệu thật từ Meta, không chạy quảng cáo, không tương tác giả.</p>
      </div>
    </div>
  );
}
