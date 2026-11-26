/* Chiến dịch tăng trưởng (Meta Growth Campaign) API client. Every call carries the studio key (same unlock as the studios);
   no Facebook token ever reaches the browser. */

import { getStudioKey, StudioError } from './studio';

const BASE = '/api/growth';

async function call<T>(method: string, path: string, body?: unknown, isForm = false): Promise<T> {
  const headers: Record<string, string> = {};
  const key = getStudioKey();
  if (key) headers['X-Studio-Key'] = key;
  if (body !== undefined && !isForm) headers['Content-Type'] = 'application/json';
  let res: Response;
  try {
    res = await fetch(`${BASE}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : isForm ? (body as FormData) : JSON.stringify(body),
    });
  } catch {
    throw new StudioError('Không kết nối được máy chủ. Kiểm tra mạng rồi thử lại.');
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  let data: any = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = null;
  }
  if (!res.ok) {
    const msg =
      data?.message || data?.title || (res.status >= 500 ? 'Máy chủ đang bận hoặc khởi động lại — thử lại sau giây lát.' : `Lỗi HTTP ${res.status}`);
    throw new StudioError(msg, res.status === 401 && !!data?.needsStudioKey);
  }
  return data as T;
}

/** Downloads a protected file (report export) with the key header. */
async function download(path: string, fallbackName: string) {
  const headers: Record<string, string> = {};
  const key = getStudioKey();
  if (key) headers['X-Studio-Key'] = key;
  const res = await fetch(`${BASE}${path}`, { headers });
  if (!res.ok) {
    const t = await res.text();
    let msg = `Lỗi HTTP ${res.status}`;
    try {
      msg = JSON.parse(t).message || msg;
    } catch {
      /* not json */
    }
    throw new StudioError(msg, res.status === 401);
  }
  const blob = await res.blob();
  const cd = res.headers.get('content-disposition') || '';
  const m = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd);
  const name = m ? decodeURIComponent(m[1]) : fallbackName;
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}

// ───────── types ─────────

export type CampaignStatus = 'Draft' | 'Running' | 'Paused' | 'Completed' | 'Cancelled';
export type ContentKind = 'Reel' | 'Photo' | 'Album' | 'Story' | 'Interactive';
export type ContentStatus = 'Draft' | 'PendingApproval' | 'Scheduled' | 'Publishing' | 'Published' | 'Failed' | 'Cancelled';
export type SourceType = 'StudioVideo' | 'AiStudioProject' | 'VideoProject';

export interface GrowthPage {
  id: string;
  pageId: string;
  pageName: string;
  isActive: boolean;
  hasToken: boolean;
  status: string;
  tokenExpiresAtUtc?: string | null;
  instagramUsername?: string | null;
  pictureUrl: string;
  latestFollowers?: number | null;
  latestFollowersDate?: string | null;
  activeCampaignId?: string | null;
  activeCampaignName?: string | null;
}

export interface Capability {
  key: string;
  feature: string;
  api: string;
  permissions: string;
  available: 'Yes' | 'No' | 'Unknown' | 'Manual';
  detail?: string | null;
}

export interface Capabilities {
  facebookAccountId: string;
  graphApiVersion: string;
  grantedPermissions: string[];
  tokenError?: string | null;
  checkedAtUtc: string;
  items: Capability[];
}

export interface PlanDay {
  day: number;
  date?: string | null;
  focus: string;
  tasks: string[];
}

export interface ChecklistItem {
  key: string;
  label: string;
  help: string;
  done: boolean;
  note?: string | null;
  atUtc?: string | null;
}

export interface MetricAvailability {
  metric: string;
  ok: boolean;
  error?: string | null;
  atUtc?: string | null;
}

export interface Campaign {
  id: string;
  name: string;
  facebookAccountId?: string | null;
  pageName: string;
  pageId?: string | null;
  pageConnected: boolean;
  status: CampaignStatus;
  startDate: string;
  endDate: string;
  totalDays: number;
  dayNumber: number;
  startedAtUtc?: string | null;
  endedAtUtc?: string | null;
  targetFollowers?: number | null;
  targetViews?: number | null;
  reelsPerDay: number;
  photosPerDay: number;
  storiesPerDay: number;
  timeSlots: string[];
  aiBudgetUsd: number;
  aiSpentUsd: number;
  approvalMode: 'Auto' | 'Review';
  alsoInstagram: boolean;
  instagramUsername?: string | null;
  startingFollowers?: number | null;
  startingFollowersAtUtc?: string | null;
  endingFollowers?: number | null;
  endingFollowersAtUtc?: string | null;
  metaOneCost?: number | null;
  metaOneCurrency: 'VND' | 'USD';
  checklist: ChecklistItem[];
  plan: PlanDay[];
  metricAvailability: MetricAvailability[];
  lastSyncedAtUtc?: string | null;
  lastSyncError?: string | null;
  notes?: string | null;
  createdAtUtc: string;
  contentCounts: Record<string, number>;
}

export interface CampaignInput {
  name?: string;
  facebookAccountId?: string;
  startDate?: string;
  days?: number;
  endDate?: string;
  targetFollowers?: number | null;
  targetViews?: number | null;
  reelsPerDay?: number;
  photosPerDay?: number;
  storiesPerDay?: number;
  timeSlots?: string[];
  aiBudgetUsd?: number;
  approvalMode?: 'Auto' | 'Review';
  alsoInstagram?: boolean;
  metaOneCost?: number | null;
  metaOneCurrency?: 'VND' | 'USD';
  notes?: string;
  plan?: PlanDay[];
}

export interface Asset {
  id: string;
  kind: 'Image' | 'Video';
  fileName: string;
  mimeType: string;
  sizeBytes: number;
  width: number;
  height: number;
  durationSeconds?: number | null;
  source: 'Upload' | 'Template';
  url: string;
  createdAtUtc: string;
}

export interface PostMetric {
  date: string;
  views?: number | null;
  reach?: number | null;
  reactions?: number | null;
  comments?: number | null;
  shares?: number | null;
  clicks?: number | null;
  follows?: number | null;
  engagement?: number | null;
  collectedAtUtc: string;
  error?: string | null;
}

export interface Content {
  id: string;
  campaignId: string;
  kind: ContentKind;
  status: ContentStatus;
  title: string;
  caption: string;
  hashtags: string;
  style?: string | null;
  cta?: string | null;
  topic?: string | null;
  sourceType?: SourceType | null;
  sourceId?: string | null;
  sourceTitle?: string | null;
  sourcePreviewUrl?: string | null;
  assets: Asset[];
  scheduledAtUtc?: string | null;
  manualPublish: boolean;
  attempts: number;
  maxAttempts: number;
  nextAttemptAtUtc?: string | null;
  lastError?: string | null;
  errorKind?: string | null;
  publishedAtUtc?: string | null;
  externalPostId?: string | null;
  permalinkUrl?: string | null;
  publishedManually: boolean;
  clipStartSeconds?: number | null;
  clipSeconds?: number | null;
  scriptId?: string | null;
  instagramMediaId?: string | null;
  instagramPermalink?: string | null;
  instagramError?: string | null;
  latestMetric?: PostMetric | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface ContentInput {
  kind?: ContentKind;
  title?: string;
  caption?: string;
  hashtags?: string;
  style?: string | null;
  cta?: string | null;
  topic?: string | null;
  sourceType?: SourceType | '' | null;
  sourceId?: string | null;
  assetIds?: string[];
  scheduledAtUtc?: string | null;
  manualPublish?: boolean;
  idempotencyKey?: string;
  submit?: boolean;
}

export interface VideoSource {
  type: SourceType;
  typeLabel: string;
  id: string;
  title: string;
  caption?: string | null;
  durationSeconds?: number | null;
  createdAtUtc: string;
  facebookAccountId?: string | null;
  alreadyPublished: boolean;
  previewUrl?: string | null;
  scheduled: boolean;
}

export interface CaptionVariant {
  style: string;
  caption: string;
  hashtags: string[];
  question?: string | null;
}

export interface CaptionResult {
  variants: CaptionVariant[];
  costUsd: number;
  campaignAiSpentUsd: number;
}

export interface RepurposeResult {
  sourceTitle: string;
  photoPosts: { headline: string; body: string; caption: string }[];
  stories: { text: string; sticker?: string | null }[];
  captions: CaptionVariant[];
  titles: string[];
  hashtags: string[];
  commentQuestion: string;
  followCta: string;
  costUsd: number;
}

export interface Suggestions {
  suggestions: string[];
  basis: string;
  fromAi: boolean;
  costUsd: number;
}

export interface Kpi {
  key: string;
  label: string;
  value?: number | null;
  unit: string;
  note?: string | null;
  source: 'meta' | 'manual' | 'app' | 'computed';
}

export interface DayPoint {
  date: string;
  followers?: number | null;
  newFollows?: number | null;
  unfollows?: number | null;
  pageViews?: number | null;
  mediaViews?: number | null;
  mediaViewers?: number | null;
  engagements?: number | null;
  contentViews?: number | null;
  contentEngagement?: number | null;
  published: number;
  manualInvitesSent?: number | null;
  manualInvitesAccepted?: number | null;
  manualPageVisits?: number | null;
  manualNewFollowers?: number | null;
}

export interface KindStats {
  kind: ContentKind;
  published: number;
  withMetrics: number;
  views?: number | null;
  reach?: number | null;
  engagement?: number | null;
  shares?: number | null;
  comments?: number | null;
  avgViews?: number | null;
  avgEngagement?: number | null;
}

export interface Period {
  from: string;
  to: string;
  followersStart?: number | null;
  followersEnd?: number | null;
  followerGain?: number | null;
  newFollows?: number | null;
  pageViews?: number | null;
  mediaViews?: number | null;
  engagements?: number | null;
  daysWithData: number;
}

export interface Overview {
  campaign: Campaign;
  page?: GrowthPage | null;
  currentFollowers?: number | null;
  currentFollowersAtUtc?: string | null;
  kpis: Kpi[];
  daily: DayPoint[];
  byKind: KindStats[];
  before?: Period | null;
  during?: Period | null;
  today: Content[];
  upcoming: Content[];
  failed: Content[];
  dueManual: Content[];
  progress: {
    day: number;
    totalDays: number;
    timePercent: number;
    planned: number;
    published: number;
    followerTargetPercent?: number | null;
    viewTargetPercent?: number | null;
  };
  insights: string[];
}

export interface PostRow {
  contentId: string;
  kind: ContentKind;
  title: string;
  captionExcerpt: string;
  captionLength: number;
  hashtagCount: number;
  style?: string | null;
  cta?: string | null;
  topic?: string | null;
  publishedAtUtc?: string | null;
  localDate?: string | null;
  localTime?: string | null;
  views?: number | null;
  reach?: number | null;
  reactions?: number | null;
  comments?: number | null;
  shares?: number | null;
  engagement?: number | null;
  follows?: number | null;
  permalinkUrl?: string | null;
  publishedManually: boolean;
  metricError?: string | null;
}

export interface Ranking {
  topViews: PostRow[];
  topReach: PostRow[];
  topShares: PostRow[];
  topEngagement: PostRow[];
  topFollows: PostRow[];
  highViewsNoFollows: PostRow[];
  weakest: PostRow[];
  followAttributionAvailable: boolean;
}

export interface Hours {
  buckets: { hour: number; label: string; posts: number; avgViews?: number | null; avgEngagement?: number | null }[];
  samples: number;
  minSamples: number;
  enoughData: boolean;
  suggestedSlots: string[];
  basis: string;
}

export interface FollowerDay {
  date: string;
  followers?: number | null;
  newFollows?: number | null;
  unfollows?: number | null;
  netChange?: number | null;
  pageVisits?: number | null;
  pageVisitsSource?: string | null;
  conversionPercent?: number | null;
  published: number;
  invitesSent?: number | null;
  invitesAccepted?: number | null;
}

export interface Followers {
  starting?: number | null;
  current?: number | null;
  gain?: number | null;
  growthPercent?: number | null;
  pageVisits?: number | null;
  conversionPercent?: number | null;
  conversionNote?: string | null;
  days: FollowerDay[];
  byKind: KindStats[];
  bestKind?: ContentKind | null;
  invitesSent?: number | null;
  invitesAccepted?: number | null;
}

export interface CompetitorSnapshot {
  id: string;
  date: string;
  followers?: number | null;
  postsPerWeek?: number | null;
  postUrl?: string | null;
  postTitle?: string | null;
  format?: string | null;
  topic?: string | null;
  reactions?: number | null;
  comments?: number | null;
  shares?: number | null;
  views?: number | null;
  note?: string | null;
}

export interface Competitor {
  id: string;
  facebookAccountId: string;
  name: string;
  url: string;
  niche?: string | null;
  notes?: string | null;
  createdAtUtc: string;
  snapshots: CompetitorSnapshot[];
  latestFollowers?: number | null;
  latestPostsPerWeek?: number | null;
}

export interface CompetitorAnalysis {
  formats: { label: string; value: number; extra?: number | null }[];
  topics: { label: string; value: number; extra?: number | null }[];
  topPosts: CompetitorSnapshot[];
  postingFrequency: { label: string; value: number }[];
  observations: number;
  aiNotes: string[];
  costUsd: number;
}

export interface ReportData {
  campaignName: string;
  pageName: string;
  pageId?: string | null;
  startDate: string;
  endDate: string;
  status: string;
  followersStart?: number | null;
  followersEnd?: number | null;
  followerGain?: number | null;
  growthPercent?: number | null;
  totalViews?: number | null;
  totalReach?: number | null;
  totalEngagement?: number | null;
  totalPublished: number;
  byKind: KindStats[];
  metaOneInvitesSent?: number | null;
  metaOneInvitesAccepted?: number | null;
  metaOneChecklist: ChecklistItem[];
  top: PostRow[];
  weak: PostRow[];
  bestSlots: string[];
  bestSlotsBasis: string;
  aiCostUsd: number;
  videoCostUsd: number;
  contentCostUsd: number;
  metaOneCost?: number | null;
  metaOneCurrency: string;
  contentCostPerFollowerUsd?: number | null;
  metaOneCostPerFollower?: number | null;
  before?: Period | null;
  during?: Period | null;
  recommendations: string[];
  dataGaps: string[];
}

export interface Report {
  id: string;
  campaignId: string;
  createdAtUtc: string;
  verdict: 'Continue' | 'TestMore' | 'DontBuy' | 'NotEnoughData';
  verdictLabel: string;
  verdictReason: string;
  aiCommentary?: string | null;
  data: ReportData;
}

export interface ActivityLog {
  id: string;
  contentId?: string | null;
  level: 'Info' | 'Warning' | 'Error';
  action: string;
  message: string;
  actor: string;
  createdAtUtc: string;
}

export interface SyncResult {
  followersRead: boolean;
  metricsAvailable: number;
  metricsUnavailable: number;
  postsRead: number;
  postErrors: number;
  errors: string[];
}

// ───────── calls ─────────

const c = (id: string) => `/campaigns/${encodeURIComponent(id)}`;

export const growth = {
  pages: () => call<GrowthPage[]>('GET', '/pages'),
  capabilities: (pageId: string) => call<Capabilities>('GET', `/pages/${pageId}/capabilities`),

  campaigns: (pageId?: string) => call<Campaign[]>('GET', `/campaigns${pageId ? `?pageId=${pageId}` : ''}`),
  campaign: (id: string) => call<Campaign>('GET', c(id)),
  createCampaign: (input: CampaignInput) => call<Campaign>('POST', '/campaigns', input),
  updateCampaign: (id: string, input: CampaignInput) => call<Campaign>('PUT', c(id), input),
  deleteCampaign: (id: string) => call<void>('DELETE', c(id)),
  action: (id: string, action: 'start' | 'pause' | 'resume' | 'complete' | 'cancel') => call<Campaign>('POST', `${c(id)}/${action}`),
  checklist: (id: string, key: string, done: boolean, note?: string) => call<Campaign>('PUT', `${c(id)}/checklist`, { key, done, note }),
  manualMetrics: (id: string, input: { date: string; invitesSent?: number | null; invitesAccepted?: number | null; pageVisits?: number | null; newFollowers?: number | null; note?: string }) =>
    call<Campaign>('PUT', `${c(id)}/manual-metrics`, input),
  logs: (id: string) => call<ActivityLog[]>('GET', `${c(id)}/logs`),

  overview: (id: string) => call<Overview>('GET', `${c(id)}/overview`),
  sync: (id: string) => call<SyncResult>('POST', `${c(id)}/sync`),
  posts: (id: string) => call<PostRow[]>('GET', `${c(id)}/analytics/posts`),
  ranking: (id: string) => call<Ranking>('GET', `${c(id)}/analytics/ranking`),
  hours: (id: string) => call<Hours>('GET', `${c(id)}/analytics/hours`),
  followers: (id: string) => call<Followers>('GET', `${c(id)}/analytics/followers`),

  contents: (id: string, q: { from?: string; to?: string; kind?: string; status?: string } = {}) => {
    const qs = new URLSearchParams(Object.entries(q).filter(([, v]) => !!v) as [string, string][]).toString();
    return call<Content[]>('GET', `${c(id)}/contents${qs ? `?${qs}` : ''}`);
  },
  createContent: (id: string, input: ContentInput) => call<Content>('POST', `${c(id)}/contents`, input),
  updateContent: (id: string, cid: string, input: ContentInput) => call<Content>('PUT', `${c(id)}/contents/${cid}`, input),
  deleteContent: (id: string, cid: string) => call<void>('DELETE', `${c(id)}/contents/${cid}`),
  contentAction: (id: string, cid: string, action: 'submit' | 'approve' | 'cancel' | 'publish-now' | 'retry') =>
    call<Content>('POST', `${c(id)}/contents/${cid}/${action}`),
  markManual: (id: string, cid: string, permalinkUrl?: string) => call<Content>('POST', `${c(id)}/contents/${cid}/mark-manual`, { permalinkUrl }),
  sources: (id: string) => call<VideoSource[]>('GET', `${c(id)}/sources`),

  assets: (id: string) => call<Asset[]>('GET', `${c(id)}/assets`),
  upload: (id: string, file: Blob, name: string, source: 'Upload' | 'Template' = 'Upload') => {
    const form = new FormData();
    form.append('file', file, name);
    form.append('source', source);
    return call<Asset>('POST', `${c(id)}/assets`, form, true);
  },
  deleteAsset: (id: string, assetId: string) => call<void>('DELETE', `${c(id)}/assets/${assetId}`),

  captions: (id: string, input: { brief?: string; sourceType?: string; sourceId?: string; styles?: string[]; variants?: number; kind?: string }) =>
    call<CaptionResult>('POST', `${c(id)}/ai/captions`, input),
  repurpose: (id: string, sourceType: SourceType, sourceId: string) => call<RepurposeResult>('POST', `${c(id)}/ai/repurpose`, { sourceType, sourceId }),
  suggestions: (id: string, ai: boolean) => call<Suggestions>('POST', `${c(id)}/ai/suggestions?ai=${ai}`),

  report: (id: string) => call<Report | undefined>('GET', `${c(id)}/report`),
  generateReport: (id: string, ai: boolean) => call<Report>('POST', `${c(id)}/report?ai=${ai}`),
  downloadReport: (id: string, format: 'csv' | 'xls') => download(`${c(id)}/report.${format}`, `bao-cao.${format}`),

  competitors: (pageId: string) => call<Competitor[]>('GET', `/competitors?pageId=${pageId}`),
  createCompetitor: (input: { facebookAccountId: string; name: string; url: string; niche?: string; notes?: string }) => call<Competitor>('POST', '/competitors', input),
  updateCompetitor: (id: string, input: { facebookAccountId: string; name?: string; url?: string; niche?: string; notes?: string }) =>
    call<Competitor>('PUT', `/competitors/${id}`, input),
  deleteCompetitor: (id: string, pageId: string) => call<void>('DELETE', `/competitors/${id}?pageId=${pageId}`),
  addSnapshot: (id: string, pageId: string, input: Partial<CompetitorSnapshot>) => call<Competitor>('POST', `/competitors/${id}/snapshots?pageId=${pageId}`, input),
  deleteSnapshot: (id: string, pageId: string, snapshotId: string) => call<void>('DELETE', `/competitors/${id}/snapshots/${snapshotId}?pageId=${pageId}`),
  analyzeCompetitors: (pageId: string, campaignId: string | undefined, ai: boolean) =>
    call<CompetitorAnalysis>('POST', `/competitors/analyze?pageId=${pageId}${campaignId ? `&campaignId=${campaignId}` : ''}&ai=${ai}`),
};

// ───────── autopilot ─────────

export interface ScriptImport {
  code: string;
  title: string;
  category?: string;
  categoryName?: string;
  mood?: string;
  era?: string;
  region?: string;
  synopsis?: string;
  hook?: string;
  endingNote?: string;
  scenes?: { title?: string; visualVi?: string; aiPromptEn?: string; voiceover?: string }[];
  source?: 'library' | 'custom' | 'ai';
}

export interface ScriptStats {
  total: number;
  used: number;
  remaining: number;
  codes: string[];
  remainingByCategory: { label: string; value: number }[];
}

export interface Notice {
  level: 'info' | 'warning' | 'error';
  code: string;
  message: string;
  atUtc: string;
}

export interface Autopilot {
  facebookAccountId: string;
  enabled: boolean;
  scriptPost: boolean;
  dailyVideo: boolean;
  storiesPerDay: number;
  lastRunAtUtc?: string | null;
  notices: Notice[];
  scripts: ScriptStats;
  campaignId?: string | null;
  today: Content[];
  slots: Record<string, string>;
}

export const autopilotApi = {
  get: (pageId: string) => call<Autopilot>('GET', `/autopilot?pageId=${pageId}`),
  set: (input: { facebookAccountId: string; enabled?: boolean; scriptPost?: boolean; dailyVideo?: boolean; storiesPerDay?: number }) =>
    call<Autopilot>('PUT', '/autopilot', input),
  run: (pageId: string) => call<Autopilot>('POST', `/autopilot/run?pageId=${pageId}`),
  scriptStats: (pageId?: string) => call<ScriptStats>('GET', `/scripts/stats${pageId ? `?pageId=${pageId}` : ''}`),
  importScripts: (items: ScriptImport[]) => call<{ added: number }>('POST', '/scripts/import', items),
};
