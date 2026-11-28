/**
 * API of "Chiến dịch kéo tương tác". Every call carries the campaign session token (X-Outreach-Session) given by the
 * server after the campaign password; the password itself never lives in the frontend. The token sits in
 * sessionStorage (gone when the tab closes) and is dropped as soon as the server says it expired.
 */

const BASE = '/api/outreach';
const KEY = 'outreach.session';
export const LOCK_EVENT = 'outreach-locked';

interface Session {
  token: string;
  expiresAtUtc: string;
}

export function getSession(): Session | null {
  try {
    const s = JSON.parse(sessionStorage.getItem(KEY) ?? 'null') as Session | null;
    if (!s || new Date(s.expiresAtUtc).getTime() <= Date.now()) return null;
    return s;
  } catch {
    return null;
  }
}

export function clearSession() {
  try {
    sessionStorage.removeItem(KEY);
  } catch {
    /* ignore */
  }
  window.dispatchEvent(new Event(LOCK_EVENT));
}

async function errorText(res: Response, fallback: string) {
  try {
    const body = await res.json();
    if (body.error || body.message) return String(body.error || body.message);
    // ASP.NET validation / server errors: say which field or what failed instead of a generic message
    if (body.errors && typeof body.errors === 'object') {
      const first = Object.values(body.errors as Record<string, string[]>).flat()[0];
      if (first) return `Dữ liệu không hợp lệ: ${first}`;
    }
    if (body.detail) return `Lỗi máy chủ: ${body.detail}`;
    return String(body.title || fallback);
  } catch {
    return res.status >= 500 ? 'Máy chủ đang bận hoặc đang khởi động lại, thử lại sau giây lát.' : fallback;
  }
}

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const session = getSession();
  if (!session) {
    clearSession();
    throw new Error('Phiên chiến dịch đã hết hạn — nhập lại mật khẩu chiến dịch.');
  }
  const res = await fetch(BASE + path, {
    method,
    headers: { 'X-Outreach-Session': session.token, ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  if (res.status === 401) {
    clearSession();
    throw new Error('Phiên chiến dịch đã hết hạn — nhập lại mật khẩu chiến dịch.');
  }
  if (!res.ok) throw new Error(await errorText(res, 'Không thực hiện được'));
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

// ── types ──
export type Fit = 'unknown' | 'fit' | 'unfit';
export type CampaignStatus = 'Draft' | 'PendingApproval' | 'Running' | 'Paused' | 'Completed';

export interface OutreachPage {
  id: string;
  pageId: string;
  name: string;
  pictureUrl: string;
  isMain: boolean;
  /** One of the campaign's helper Pages (Trạm Tin Showbiz / Trạm Tin Công Nghệ). */
  isHelper: boolean;
  status: string;
  health: 'ok' | 'warning' | 'error';
  problem?: string | null;
  permissions: string[];
  tokenExpiresAtUtc?: string | null;
}

export interface Group {
  id: string;
  name: string;
  url: string;
  key: string;
  topic?: string | null;
  exchangeTypes: string[];
  fit: Fit;
  privacy: 'public' | 'private' | 'unknown';
  members?: number | null;
  rules?: string | null;
  note?: string | null;
  createdAtUtc: string;
  views: number;
  clicks: number;
  /** Which helper Pages are in this Group (marked by hand). */
  memberships: Membership[];
}

export type MembershipStatus = 'requested' | 'joined' | 'rejected';

export interface Membership {
  pageId: string;
  status: MembershipStatus;
  updatedAtUtc: string;
}

export interface DiscoveredGroup {
  name: string;
  url: string;
  description?: string | null;
  members?: number | null;
  privacy: 'public' | 'private' | 'unknown';
  /** Already in the Group list. */
  groupId?: string | null;
}

export interface DiscoverResult {
  keyword: string;
  page: number;
  hasMore: boolean;
  facebookSearchUrl: string;
  results: DiscoveredGroup[];
}

export type GroupInput = Partial<Omit<Group, 'id' | 'key' | 'createdAtUtc' | 'views' | 'clicks' | 'memberships'>> & { name: string; url: string };

export interface Content {
  id: string;
  text: string;
  style: string;
  topic?: string | null;
  origin: 'ai' | 'manual';
  copiedCount: number;
  createdAtUtc: string;
}

export interface Draft {
  text: string;
  similarity: number;
  similarTo?: string | null;
}

export interface Campaign {
  id: string;
  name: string;
  slug: string;
  pageIds: string[];
  groupIds: string[];
  targetPath: string;
  contentId?: string | null;
  contentText?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  status: CampaignStatus;
  owner?: string | null;
  resultNote?: string | null;
  createdAtUtc: string;
  views: number;
  clicks: number;
  activitiesDone: number;
  activitiesPlanned: number;
}

export type CampaignInput = Partial<Omit<Campaign, 'id' | 'createdAtUtc' | 'views' | 'clicks' | 'activitiesDone' | 'activitiesPlanned' | 'targetPath'>> & { name: string };

export interface Activity {
  id: string;
  groupId: string;
  pageId?: string | null;
  kind: 'post' | 'join' | 'comment';
  postUrl?: string | null;
  note?: string | null;
  doneAtUtc: string;
}

export interface CampaignLink {
  groupId: string;
  groupName: string;
  groupUrl: string;
  pageId?: string | null;
  pageName?: string | null;
  utmContent: string;
  /** Is this helper Page in the Group: null = not asked yet. */
  membership?: MembershipStatus | null;
  /** Times this campaign's wall posts were shared into the Group by this Page. */
  shares: number;
  lastSharedAtUtc?: string | null;
  link: string;
  views: number;
  clicks: number;
  activities: Activity[];
}

export interface WallPost {
  id: string;
  pageId: string;
  pageName: string;
  campaignId?: string | null;
  postId: string;
  permalinkUrl: string;
  text: string;
  createdAtUtc: string;
  shares: { groupId: string; sharedAtUtc: string }[];
}

export interface PostTask {
  id: string;
  groupId?: string | null;
  groupName?: string | null;
  campaignId?: string | null;
  postUrl: string;
  postText?: string | null;
  suggestedComment?: string | null;
  status: 'todo' | 'done' | 'skipped';
  doneAtUtc?: string | null;
  note?: string | null;
  createdAtUtc: string;
}

export interface Breakdown {
  key: string;
  label?: string | null;
  views: number;
  clicks: number;
}

export interface Stats {
  days: number;
  views: number;
  clicks: number;
  uniqueVisitors: number;
  mobileViews: number;
  desktopViews: number;
  perDay: { date: string; views: number; clicks: number }[];
  sources: Breakdown[];
  campaigns: Breakdown[];
  contents: Breakdown[];
}

export interface Overview {
  groups: number;
  groupsFit: number;
  contents: number;
  campaignsRunning: number;
  campaigns: number;
  postTasksTodo: number;
  views7d: number;
  clicks7d: number;
  outreachViews7d: number;
  outreachClicks7d: number;
  pages: OutreachPage[];
}

export interface Security {
  passwordConfigured: boolean;
  passwordSource: 'database' | 'config';
  sessionHours: number;
  maxAttempts: number;
  lockMinutes: number;
  recentAttempts: { atUtc: string; ip: string; success: boolean }[];
}

export interface UnlockResult {
  ok: boolean;
  token?: string | null;
  expiresAtUtc?: string | null;
  error?: string | null;
  attemptsLeft?: number | null;
  lockedUntilUtc?: string | null;
}

// ── calls ──
export const outreach = {
  async unlock(password: string): Promise<UnlockResult> {
    const res = await fetch(`${BASE}/auth/unlock`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ password }),
    });
    let body: UnlockResult;
    try {
      body = await res.json();
    } catch {
      body = { ok: false, error: res.status >= 500 ? 'Máy chủ đang bận, thử lại sau.' : 'Không mở khoá được.' };
    }
    if (body.ok && body.token && body.expiresAtUtc) {
      sessionStorage.setItem(KEY, JSON.stringify({ token: body.token, expiresAtUtc: body.expiresAtUtc }));
    }
    return body;
  },
  lock: () => clearSession(),

  overview: () => call<Overview>('GET', '/overview'),
  pages: (refresh = false) => call<OutreachPage[]>('GET', `/pages${refresh ? '?refresh=true' : ''}`),

  groups: () => call<Group[]>('GET', '/groups'),
  addGroup: (g: GroupInput) => call<Group>('POST', '/groups', g),
  editGroup: (id: string, g: GroupInput) => call<Group>('PUT', `/groups/${id}`, g),
  deleteGroup: (id: string) => call<void>('DELETE', `/groups/${id}`),
  importCsv: (csv: string) => call<{ added: number; duplicates: number; invalid: string[] }>('POST', '/groups/import', { csv }),
  discover: (keyword: string, page = 1) => call<DiscoverResult>('GET', `/groups/discover?keyword=${encodeURIComponent(keyword)}&page=${page}`),
  setMembership: (groupId: string, pageId: string, status: MembershipStatus | 'none') =>
    call<Membership>('PUT', `/groups/${groupId}/membership/${pageId}`, { status }),
  searchGroups: (keyword: string) =>
    call<{ keyword: string; facebookSearchUrl: string; relatedKeywords: string[] }>('GET', `/groups/search?keyword=${encodeURIComponent(keyword)}`),

  contents: () => call<Content[]>('GET', '/contents'),
  saveContent: (c: { text: string; style?: string; topic?: string | null; origin?: string }) => call<Content>('POST', '/contents', c),
  deleteContent: (id: string) => call<void>('DELETE', `/contents/${id}`),
  copied: (id: string) => call<void>('POST', `/contents/${id}/copied`),
  variants: (count: number, extra?: string) => call<Draft[]>('POST', '/contents/variants', { count, extra }),
  write: (r: { style: string; topic?: string; groupId?: string; count: number; extra?: string }) => call<Draft[]>('POST', '/contents/write', r),
  check: (text: string) => call<Draft>('POST', '/contents/check', { text }),

  campaigns: () => call<Campaign[]>('GET', '/campaigns'),
  addCampaign: (c: CampaignInput) => call<Campaign>('POST', '/campaigns', c),
  editCampaign: (id: string, c: CampaignInput) => call<Campaign>('PUT', `/campaigns/${id}`, c),
  deleteCampaign: (id: string) => call<void>('DELETE', `/campaigns/${id}`),
  links: (id: string) => call<CampaignLink[]>('GET', `/campaigns/${id}/links`),
  addActivity: (id: string, a: { groupId: string; pageId?: string | null; kind: string; postUrl?: string; note?: string }) =>
    call<Activity>('POST', `/campaigns/${id}/activities`, a),
  deleteActivity: (id: string) => call<void>('DELETE', `/activities/${id}`),

  /** Publishes picture + text on a helper Page's wall (Graph API). Sharing into Groups is then done by hand. */
  async publishWallPost(pageId: string, message: string, image: Blob, campaignId?: string | null): Promise<WallPost> {
    const session = getSession();
    if (!session) {
      clearSession();
      throw new Error('Phiên chiến dịch đã hết hạn — nhập lại mật khẩu chiến dịch.');
    }
    const form = new FormData();
    form.append('pageId', pageId);
    form.append('message', message);
    if (campaignId) form.append('campaignId', campaignId);
    form.append('image', image, 'bai-dang.jpg');
    const res = await fetch(`${BASE}/wall-posts`, { method: 'POST', headers: { 'X-Outreach-Session': session.token }, body: form });
    if (res.status === 401) {
      clearSession();
      throw new Error('Phiên chiến dịch đã hết hạn — nhập lại mật khẩu chiến dịch.');
    }
    if (!res.ok) throw new Error(await errorText(res, 'Chưa đăng được bài'));
    return await res.json();
  },
  wallPosts: (pageId?: string) => call<WallPost[]>('GET', `/wall-posts${pageId ? `?pageId=${pageId}` : ''}`),
  setShare: (wallPostId: string, groupId: string, shared: boolean) =>
    call<{ groupId: string; sharedAtUtc: string }>('PUT', `/wall-posts/${wallPostId}/shares/${groupId}`, { shared }),

  posts: (status?: string) => call<PostTask[]>('GET', `/posts${status ? `?status=${status}` : ''}`),
  addPost: (p: Partial<PostTask> & { postUrl: string }) => call<PostTask>('POST', '/posts', p),
  editPost: (id: string, p: Partial<PostTask> & { postUrl: string }) => call<PostTask>('PUT', `/posts/${id}`, p),
  deletePost: (id: string) => call<void>('DELETE', `/posts/${id}`),
  suggest: (id: string) => call<string[]>('POST', `/posts/${id}/suggest`),

  stats: (days: number) => call<Stats>('GET', `/stats?days=${days}`),

  security: () => call<Security>('GET', '/security'),
  changeSecurity: (b: { currentPassword: string; newPassword?: string; sessionHours?: number }) => call<void>('PUT', '/security', b),
  lockAll: () => call<void>('POST', '/security/lock-all'),
};

export const EXCHANGE_TYPES: { value: string; label: string }[] = [
  { value: 'follow', label: 'Follow' },
  { value: 'like', label: 'Like' },
  { value: 'comment', label: 'Comment' },
  { value: 'share', label: 'Share' },
  { value: 'view', label: 'View' },
  { value: 'mixed', label: 'Tổng hợp' },
];

export const STATUS_LABEL: Record<CampaignStatus, string> = {
  Draft: 'Nháp',
  PendingApproval: 'Chờ duyệt',
  Running: 'Đang chạy',
  Paused: 'Tạm dừng',
  Completed: 'Hoàn thành',
};

export async function copyText(text: string) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const ta = document.createElement('textarea');
    ta.value = text;
    document.body.appendChild(ta);
    ta.select();
    const ok = document.execCommand('copy');
    ta.remove();
    return ok;
  }
}

export const MEMBERSHIP_LABEL: Record<MembershipStatus, string> = {
  requested: 'Đã xin vào',
  joined: 'Đã vào',
  rejected: 'Bị từ chối',
};
