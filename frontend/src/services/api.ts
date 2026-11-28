import { SystemConfig, VideoProject, VideoProjectStatus } from '../types';

const API_BASE = '/api';

/**
 * Builds a readable error message from a failed response. When the backend is down, the Vite dev
 * proxy answers with a bare 5xx (no JSON body) — say that plainly instead of a generic failure.
 */
async function apiError(res: Response, fallback: string): Promise<string> {
  const text = await res.text().catch(() => '');
  try {
    const body = JSON.parse(text);
    const msg = body.error || body.message || (body.detail && !body.detail.includes('Please contact administrator') ? body.detail : null) || body.title;
    if (msg) return String(msg);
  } catch {
    // not JSON
  }
  if (res.status >= 500) {
    return 'Máy chủ backend đang xử lý hoặc đang khởi động lại. Vui lòng tải lại trang sau giây lát.';
  }
  return fallback;
}

export interface CreateProjectRequest {
  title: string;
  sourceUrl: string;
  publisher?: string;
  targetDurationSeconds?: number;
  aiProvider?: string;
  voice?: string;
  voiceSpeed?: string;
  /** Page the video is made for (its name is on the video, its topic sets the colours) */
  facebookAccountId?: string;
}

export interface ProcessingJobDto {
  id: string;
  videoProjectId: string;
  hangfireJobId?: string;
  step: string;
  status: string;
  retryCount: number;
  progressPercentage: number;
  startedAtUtc?: string;
  finishedAtUtc?: string;
  errorMessage?: string;
}


export interface WorkerSide {
  online: boolean;
  /** the program runs, even while it stays out of the work (e.g. updating to the new version) */
  seen: boolean;
  working: boolean;
  version?: string | null;
  machine?: string | null;
  heartbeatUtc?: string | null;
  seenUtc?: string | null;
  runningJobs: string[];
}

/** Who runs the background jobs right now: the owner's PC or Railway. */
export interface WorkerStatus {
  worker: 'pc' | 'cloud' | 'both' | 'none';
  webVersion: string;
  pc: WorkerSide;
  cloud: WorkerSide;
  nowUtc: string;
}

export const api = {
  async getWorkerStatus(): Promise<WorkerStatus | null> {
    try {
      const res = await fetch(`${API_BASE}/system/worker`);
      return res.ok ? await res.json() : null;
    } catch {
      return null;
    }
  },

  async getHealth(): Promise<boolean> {
    try {
      const res = await fetch('/health');
      return res.ok;
    } catch {
      return false;
    }
  },

  async getDetailedHealth() {
    try {
      const res = await fetch(`${API_BASE}/health`);
      if (!res.ok) throw new Error('Health check failed');
      return await res.json();
    } catch (e) {
      return { status: 'Unreachable', environment: 'Unknown', components: {} };
    }
  },

  async getConfigSummary(): Promise<SystemConfig> {
    try {
      const res = await fetch(`${API_BASE}/system/config-summary`);
      if (!res.ok) throw new Error('Config summary failed');
      return await res.json();
    } catch (e) {
      return {
        environment: 'Development',
        ai: { provider: 'Gemini', isConfigured: false, model: 'gemini-1.5-flash' },
        tts: { provider: 'EdgeTts', voice: 'vi-VN-HoaiMyNeural', isConfigured: true },
        facebook: { isConfigured: false, pageId: '' },
        storage: { tempPath: './tmp/newsstudio', maxConcurrentRenders: 2, maxStorageMb: 5120, ttlMinutes: 120 }
      };
    }
  },

  /** "Tin đang hot" — ranked stories from Vietnamese and international RSS feeds. */
  async getTrending(params: { region?: string; source?: string; q?: string; hours?: number; limit?: number; topic?: string } = {}): Promise<TrendingOverviewDto> {
    const qs = new URLSearchParams();
    Object.entries(params).forEach(([k, v]) => {
      if (v !== undefined && v !== null && v !== '') qs.append(k, String(v));
    });
    try {
      const res = await fetch(`${API_BASE}/trending?${qs}`);
      if (!res.ok) {
        return { items: [], sources: [], trendKeywords: [] };
      }
      return await res.json();
    } catch {
      return { items: [], sources: [], trendKeywords: [] };
    }
  },

  // ── Chéo lại: lượt bấm "Mở Facebook – ủng hộ Page" trên trang public ──
  async getLandingClicks(slug: string, status: 'waiting' | 'done' | 'all', days = 30): Promise<LandingClickDto[]> {
    const res = await fetch(`${API_BASE}/landing/${encodeURIComponent(slug)}/clicks?status=${status}&days=${days}`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được danh sách lượt bấm'));
    return await res.json();
  },

  async setLandingClickHandled(id: string, handled: boolean): Promise<void> {
    const res = await fetch(`${API_BASE}/landing/clicks/${id}/handled`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ handled }),
    });
    if (!res.ok) throw new Error(await apiError(res, 'Chưa lưu được'));
  },

  async getLandingStats(slug: string, days = 30): Promise<LandingStatsDto> {
    const res = await fetch(`${API_BASE}/landing/${encodeURIComponent(slug)}/stats?days=${days}`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được thống kê'));
    return await res.json();
  },

  // ── Lượt xem theo Page ──
  async getComments(status: string): Promise<PostCommentDto[]> {
    const res = await fetch(`${API_BASE}/engagement/comments?status=${encodeURIComponent(status)}`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được bình luận'));
    return await res.json();
  },

  async replyComment(id: string, message: string): Promise<PostCommentDto> {
    const res = await fetch(`${API_BASE}/engagement/comments/${id}/reply`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message }),
    });
    if (!res.ok) throw new Error(await apiError(res, 'Không gửi được câu trả lời'));
    return await res.json();
  },

  async dismissComment(id: string): Promise<void> {
    await fetch(`${API_BASE}/engagement/comments/${id}/dismiss`, { method: 'POST' });
  },

  async refreshEngagement(): Promise<void> {
    await fetch(`${API_BASE}/engagement/refresh`, { method: 'POST' });
  },

  async getEngagementSettings(): Promise<{ firstComment: boolean }> {
    const res = await fetch(`${API_BASE}/engagement/settings`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được cài đặt'));
    return await res.json();
  },

  async saveEngagementSettings(settings: { firstComment: boolean }): Promise<void> {
    const res = await fetch(`${API_BASE}/engagement/settings`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    });
    if (!res.ok) throw new Error(await apiError(res, 'Không lưu được cài đặt'));
  },

  async getPageStats(days = 14): Promise<PageStatsOverviewDto> {
    const res = await fetch(`${API_BASE}/facebook/page-stats?days=${days}`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được thống kê lượt xem'));
    return await res.json();
  },

  async syncPageStats(): Promise<PageStatsSyncResultDto> {
    const res = await fetch(`${API_BASE}/facebook/page-stats/sync`, { method: 'POST' });
    if (!res.ok) throw new Error(await apiError(res, 'Không cập nhật được số liệu từ Facebook'));
    return await res.json();
  },

  // ── Tự động đăng (nhiều Page) ──
  async getAutoPost(): Promise<AutoPostOverviewDto> {
    const res = await fetch(`${API_BASE}/auto-post`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được lịch tự động đăng'));
    return await res.json();
  },

  async saveAutoPostSettings(facebookAccountId: string, settings: AutoPostSettingsDto): Promise<AutoPostSettingsDto> {
    const res = await fetch(`${API_BASE}/auto-post/pages/${facebookAccountId}/settings`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    });
    if (!res.ok) throw new Error(await apiError(res, 'Lưu cài đặt tự động đăng thất bại'));
    return await res.json();
  },

  async suggestAutoPostSlots(count: number, offset = 0): Promise<string[]> {
    const res = await fetch(`${API_BASE}/auto-post/suggest-slots?count=${count}&offset=${offset}`);
    if (!res.ok) throw new Error(await apiError(res, 'Không gợi ý được khung giờ'));
    return await res.json();
  },

  async autoPostRunNow(facebookAccountId: string): Promise<AutoPostRunDto> {
    const res = await fetch(`${API_BASE}/auto-post/pages/${facebookAccountId}/run-now`, { method: 'POST' });
    if (!res.ok) throw new Error(await apiError(res, 'Không bắt đầu được lượt đăng'));
    return await res.json();
  },

  // ── Bản tin tối (Điểm tin nóng) ──
  async getDailyDigests(pageId?: string): Promise<DailyDigestRunDto[]> {
    const res = await fetch(`${API_BASE}/daily-digest${pageId ? `?pageId=${pageId}` : ''}`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được bản tin'));
    return await res.json();
  },

  async previewDailyDigest(facebookAccountId: string, kind?: 'Daily' | 'Weekly'): Promise<DailyDigestRunDto> {
    const res = await fetch(`${API_BASE}/daily-digest/pages/${facebookAccountId}/preview${kind ? `?kind=${kind}` : ''}`, { method: 'POST' });
    if (!res.ok) throw new Error(await apiError(res, 'Không tạo được bản tin thử'));
    return await res.json();
  },

  async publishDailyDigest(facebookAccountId: string): Promise<DailyDigestRunDto> {
    const res = await fetch(`${API_BASE}/daily-digest/pages/${facebookAccountId}/publish`, { method: 'POST' });
    if (!res.ok) throw new Error(await apiError(res, 'Không đăng được bản tin'));
    return await res.json();
  },

  async getFactCheck(projectId: string): Promise<FactCheckReportDto> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/fact-check`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được kết quả kiểm chứng'));
    return await res.json();
  },

  async runFactCheck(projectId: string, ai: boolean): Promise<FactCheckReportDto> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/fact-check?ai=${ai}`, { method: 'POST' });
    if (!res.ok) throw new Error(await apiError(res, 'Không kiểm chứng được'));
    return await res.json();
  },

  async getAiUsage(): Promise<AiUsageReportDto> {
    const res = await fetch(`${API_BASE}/ai-usage`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được chi phí AI'));
    return await res.json();
  },

  async setAiBudget(budget: AiBudgetSettingsDto): Promise<AiBudgetSettingsDto> {
    const res = await fetch(`${API_BASE}/ai-usage/budget`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(budget),
    });
    if (!res.ok) throw new Error(await apiError(res, 'Không lưu được ngân sách AI'));
    return await res.json();
  },

  pageLogoUrl(facebookAccountId: string, version = 0): string {
    return `${API_BASE}/auto-post/pages/${facebookAccountId}/logo?v=${version}`;
  },

  async uploadPageLogo(facebookAccountId: string, file: File): Promise<void> {
    const form = new FormData();
    form.append('file', file);
    const res = await fetch(`${API_BASE}/auto-post/pages/${facebookAccountId}/logo`, { method: 'PUT', body: form });
    if (!res.ok) throw new Error(await apiError(res, 'Không tải logo lên được'));
  },

  async deletePageLogo(facebookAccountId: string): Promise<void> {
    const res = await fetch(`${API_BASE}/auto-post/pages/${facebookAccountId}/logo`, { method: 'DELETE' });
    if (!res.ok) throw new Error(await apiError(res, 'Không xoá được logo'));
  },

  async getPageInsights(facebookAccountId: string): Promise<PageInsightsDto> {
    const res = await fetch(`${API_BASE}/auto-post/pages/${facebookAccountId}/insights`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được số liệu học được'));
    return await res.json();
  },

  /** AI checks the video against the article and writes caption + first comment. */
  async editorialPass(projectId: string): Promise<EditorialPassDto> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/editorial-pass`, { method: 'POST' });
    if (!res.ok) throw new Error(await apiError(res, 'AI chưa kiểm tra được'));
    return await res.json();
  },

  async cancelAutoPostRun(id: string): Promise<void> {
    const res = await fetch(`${API_BASE}/auto-post/runs/${id}/cancel`, { method: 'POST' });
    if (!res.ok) throw new Error(await apiError(res, 'Không huỷ được lượt đăng'));
  },

  async getNewsTopics(): Promise<NewsTopicDto[]> {
    const res = await fetch(`${API_BASE}/trending/topics`);
    if (!res.ok) throw new Error(await apiError(res, 'Không tải được danh sách chủ đề'));
    return await res.json();
  },

  async refreshTrending(): Promise<TrendingRefreshResultDto> {
    const res = await fetch(`${API_BASE}/trending/refresh`, { method: 'POST' });
    if (!res.ok) throw new Error(await apiError(res, 'Cập nhật tin hot thất bại'));
    return await res.json();
  },

  /** Gemini models the server's API key can actually use (live from Google). */
  async getAiModels(): Promise<AiModelsDto> {
    const res = await fetch(`${API_BASE}/system/ai-models`);
    if (!res.ok) throw new Error(await apiError(res, 'Không lấy được danh sách model AI'));
    return await res.json();
  },

  async getSystemInfo() {
    try {
      const res = await fetch(`${API_BASE}/system/info`);
      if (!res.ok) throw new Error('System info failed');
      return await res.json();
    } catch (e) {
      return { appName: 'Facebook chéo uy tính', version: '1.0.0', dotnetVersion: '8.0' };
    }
  },

  // Phase 2 Video Projects & Jobs APIs
  async createVideoProject(data: CreateProjectRequest): Promise<VideoProject> {
    const res = await fetch(`${API_BASE}/video-projects`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Failed to create video project'));
    }
    return await res.json();
  },

  async getVideoProjects(page = 1, pageSize = 20): Promise<VideoProject[]> {
    try {
      const res = await fetch(`${API_BASE}/video-projects?page=${page}&pageSize=${pageSize}`);
      if (!res.ok) return [];
      return await res.json();
    } catch {
      return [];
    }
  },

  async getVideoProjectById(id: string): Promise<VideoProject | null> {
    try {
      const res = await fetch(`${API_BASE}/video-projects/${id}`);
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  async processVideoProject(id: string): Promise<ProcessingJobDto> {
    const res = await fetch(`${API_BASE}/video-projects/${id}/process`, {
      method: 'POST'
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Failed to enqueue processing job'));
    }
    return await res.json();
  },

  async getJobById(id: string): Promise<ProcessingJobDto | null> {
    try {
      const res = await fetch(`${API_BASE}/jobs/${id}`);
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  async cancelJob(id: string): Promise<boolean> {
    try {
      const res = await fetch(`${API_BASE}/jobs/${id}/cancel`, {
        method: 'POST'
      });
      return res.ok;
    } catch {
      return false;
    }
  },

  // Phase 3 Article & Image Extractor APIs
  async extractArticle(url: string): Promise<ArticleDto> {
    const res = await fetch(`${API_BASE}/articles/extract`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url })
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Trích xuất bài báo thất bại'));
    }
    return await res.json();
  },

  async getArticles(page = 1, pageSize = 20): Promise<ArticleDto[]> {
    try {
      const res = await fetch(`${API_BASE}/articles?page=${page}&pageSize=${pageSize}`);
      if (!res.ok) return [];
      return await res.json();
    } catch {
      return [];
    }
  },

  async getArticleById(id: string): Promise<ArticleDto | null> {
    try {
      const res = await fetch(`${API_BASE}/articles/${id}`);
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  async updateArticleImage(articleId: string, imageId: string, data: UpdateArticleImageRequest): Promise<ArticleImageDto> {
    const res = await fetch(`${API_BASE}/articles/${articleId}/images/${imageId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Cập nhật thông tin ảnh thất bại'));
    }
    return await res.json();
  },

  // Phase 4 AI Editor & Script Planner APIs
  async generateScript(projectId: string, options?: { aiProvider?: string; modelName?: string; targetDurationSeconds?: number }): Promise<VideoScriptDto> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/script/generate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(options || {})
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Tạo kịch bản AI thất bại'));
    }
    return await res.json();
  },

  async getScript(projectId: string): Promise<VideoScriptDto | null> {
    try {
      const res = await fetch(`${API_BASE}/video-projects/${projectId}/script`);
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  async updateScript(projectId: string, data: Partial<VideoScriptDto>): Promise<VideoScriptDto> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/script`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Cập nhật kịch bản thất bại'));
    }
    return await res.json();
  },

  async planScenes(projectId: string): Promise<VideoScenePlanDto[]> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/scenes/plan`, {
      method: 'POST'
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Phân cảnh kịch bản thất bại'));
    }
    return await res.json();
  },

  async getScenes(projectId: string): Promise<VideoScenePlanDto[]> {
    try {
      const res = await fetch(`${API_BASE}/video-projects/${projectId}/scenes`);
      if (!res.ok) return [];
      return await res.json();
    } catch {
      return [];
    }
  },

  async updateScenes(projectId: string, scenes: VideoScenePlanDto[]): Promise<VideoScenePlanDto[]> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/scenes`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ scenes })
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Cập nhật phân cảnh thất bại'));
    }
    return await res.json();
  },

  // Phase 5 Video Rendering & Ephemeral Media APIs
  async renderVideo(projectId: string, ttsProvider?: string, voice?: string, speed?: string): Promise<RenderStatusResponseDto> {
    const params = new URLSearchParams();
    if (ttsProvider) params.append('ttsProvider', ttsProvider);
    if (voice) params.append('voice', voice);
    if (speed) params.append('speed', speed);
    const queryString = params.toString();
    const url = `${API_BASE}/video-projects/${projectId}/render${queryString ? `?${queryString}` : ''}`;
    const res = await fetch(url, { method: 'POST' });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Yêu cầu render video thất bại'));
    }
    return await res.json();
  },

  async getRenderStatus(projectId: string): Promise<RenderStatusResponseDto> {
    try {
      const res = await fetch(`${API_BASE}/video-projects/${projectId}/render-status`);
      if (!res.ok) return { projectId, status: 'Unknown', progressPercentage: 0 };
      return await res.json();
    } catch {
      return { projectId, status: 'Unknown', progressPercentage: 0 };
    }
  },

  async getAssets(projectId: string): Promise<VideoAssetDto[]> {
    try {
      const res = await fetch(`${API_BASE}/video-projects/${projectId}/assets`);
      if (!res.ok) return [];
      return await res.json();
    } catch {
      return [];
    }
  },

  async cleanupAssets(projectId: string): Promise<boolean> {
    try {
      const res = await fetch(`${API_BASE}/video-projects/${projectId}/assets`, { method: 'DELETE' });
      return res.ok;
    } catch {
      return false;
    }
  },

  // Phase 6 Video Review & Quality Control APIs
  async getReview(projectId: string): Promise<VideoReviewDto | null> {
    try {
      const res = await fetch(`${API_BASE}/video-projects/${projectId}/review`);
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  async evaluateQuality(projectId: string): Promise<VideoReviewDto> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/review/evaluate`, { method: 'POST' });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Kiểm định chất lượng video thất bại'));
    }
    return await res.json();
  },

  async approveReview(projectId: string, reviewer = 'Editor', notes = 'Đạt chuẩn biên tập'): Promise<VideoReviewDto> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/review/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ decision: 'Approved', reviewedBy: reviewer, feedbackNotes: notes })
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Không thể phê duyệt video do lỗi nghiêm trọng'));
    }
    return await res.json();
  },

  async rejectReview(projectId: string, reviewer = 'Editor', notes: string): Promise<VideoReviewDto> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/review/reject`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ decision: 'Rejected', reviewedBy: reviewer, feedbackNotes: notes })
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Gửi từ chối video thất bại'));
    }
    return await res.json();
  },

  async requestReviewAction(projectId: string, action: string, notes?: string): Promise<boolean> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/review/request-action`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ action, notes })
    });
    if (!res.ok) {
      throw new Error(await apiError(res, `Yêu cầu thực hiện ${action} thất bại`));
    }
    return true;
  },

  async updateVideoMetadata(projectId: string, data: { title: string; hook?: string; caption?: string }): Promise<boolean> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/review/metadata`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Cập nhật tiêu đề & caption thất bại'));
    }
    return true;
  },

  async deleteVideoProject(projectId: string): Promise<boolean> {
    try {
      const res = await fetch(`${API_BASE}/video-projects/${projectId}`, { method: 'DELETE' });
      return res.ok;
    } catch {
      return false;
    }
  },

  // Phase 7 Facebook Graph API & Reels Publishing
  async getFacebookPages(): Promise<FacebookAccountDto[]> {
    try {
      const res = await fetch(`${API_BASE}/facebook/pages`);
      if (!res.ok) return [];
      return await res.json();
    } catch {
      return [];
    }
  },

  async connectFacebookPage(data: ConnectFacebookPageRequestDto): Promise<FacebookAccountDto> {
    const res = await fetch(`${API_BASE}/facebook/pages`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data)
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Kết nối Facebook Page thất bại'));
    }
    return await res.json();
  },

  /** Reads again which Instagram account is linked to each connected page. */
  async refreshInstagram(): Promise<FacebookAccountDto[]> {
    const res = await fetch(`${API_BASE}/facebook/pages/instagram/refresh`, { method: 'POST' });
    if (!res.ok) throw new Error(await apiError(res, 'Không đọc được Instagram của các Page'));
    return await res.json();
  },

  /** Kết nối nhanh: list pages of a user token (pageIds empty) or connect the given pages. */
  async connectFromUserToken(userAccessToken: string, pageIds: string[] = []): Promise<UserTokenPagesResultDto> {
    const res = await fetch(`${API_BASE}/facebook/connect-from-user-token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userAccessToken, pageIds }),
    });
    if (!res.ok) throw new Error(await apiError(res, 'Không đọc được mã người dùng'));
    return await res.json();
  },

  async verifyFacebookToken(token: string): Promise<VerifyTokenResultDto> {
    const res = await fetch(`${API_BASE}/facebook/verify`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ pageAccessToken: token })
    });
    if (!res.ok) {
      return { isValid: false, errorMessage: 'Không thể xác thực token với Meta' };
    }
    return await res.json();
  },

  async disconnectFacebookPage(id: string, permanent = false): Promise<boolean> {
    try {
      const res = await fetch(`${API_BASE}/facebook/pages/${id}${permanent ? '?permanent=true' : ''}`, { method: 'DELETE' });
      return res.ok;
    } catch {
      return false;
    }
  },

  async publishReel(projectId: string, options?: PublishReelRequestDto): Promise<PublishReelResultDto> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/publish-reel`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(options || {})
    });
    if (!res.ok) {
      throw new Error(await apiError(res, 'Xuất bản Facebook Reels thất bại'));
    }
    return await res.json();
  },

  async getProjectFacebookPosts(projectId: string): Promise<FacebookPostDto[]> {
    try {
      const res = await fetch(`${API_BASE}/video-projects/${projectId}/facebook-posts`);
      if (!res.ok) return [];
      return await res.json();
    } catch {
      return [];
    }
  },

  async fetchPostMetrics(postId: string): Promise<VideoMetricDto> {
    const res = await fetch(`${API_BASE}/facebook/posts/${postId}/metrics`);
    if (!res.ok) {
      throw new Error(await apiError(res, 'Cập nhật số liệu Insights thất bại'));
    }
    return await res.json();
  },

  // Phase 8: Production Monitoring, System Metrics & Maintenance
  async getSystemMetrics(): Promise<SystemMetricsDto> {
    const res = await fetch(`${API_BASE}/system/metrics`);
    if (!res.ok) throw new Error('Không thể lấy chỉ số hệ thống');
    return await res.json();
  },

  async getSystemCosts(): Promise<SystemCostSummaryDto> {
    const res = await fetch(`${API_BASE}/system/costs`);
    if (!res.ok) throw new Error('Không thể lấy thống kê chi phí');
    return await res.json();
  },

  async triggerStorageCleanup(): Promise<StorageCleanupReportDto> {
    const res = await fetch(`${API_BASE}/system/storage/cleanup`, { method: 'POST' });
    if (!res.ok) throw new Error('Dọn dẹp bộ nhớ tạm thất bại');
    return await res.json();
  },

  async triggerJobRecovery(): Promise<JobRecoveryReportDto> {
    const res = await fetch(`${API_BASE}/system/jobs/recover`, { method: 'POST' });
    if (!res.ok) throw new Error('Khôi phục job thất bại');
    return await res.json();
  },
};

export interface TrendingArticleDto {
  id: string;
  url: string;
  title: string;
  summary?: string | null;
  imageUrl?: string | null;
  sourceKey: string;
  sourceName: string;
  region: 'VN' | 'World' | string;
  language: string;
  publishedAtUtc: string;
  isFeatured: boolean;
  coverageCount: number;
  relatedSources: string[];
  trendKeyword?: string | null;
  trendTraffic?: number | null;
  hotScore: number;
  reasons: string[];
  /** Topic keys, e.g. "the-thao" */
  topics?: string[];
}

export interface TrendingSourceDto {
  key: string;
  name: string;
  region: string;
  language: string;
  itemCount: number;
}

export interface TrendingRefreshResultDto {
  startedAtUtc: string;
  finishedAtUtc: string;
  feedsOk: number;
  feedsFailed: number;
  failedFeeds: string[];
  itemsFetched: number;
  newItems: number;
  trendKeywords: number;
  totalStored: number;
}

export interface TrendingOverviewDto {
  items: TrendingArticleDto[];
  sources: TrendingSourceDto[];
  lastRefresh?: TrendingRefreshResultDto | null;
  trendKeywords: string[];
}

export interface AiModelsDto {
  configured: boolean;
  current: string;
  currentAvailable: boolean;
  models: { id: string; displayName: string }[];
}

export interface SystemMetricsDto {
  cpuUsagePercent: number;
  memoryWorkingSetMb: number;
  tempStorageUsedMb: number;
  tempStorageLimitMb: number;
  tempStorageUsagePercent: number;
  tempStoragePath: string;
  activeJobsCount: number;
  pendingJobsCount: number;
  completedJobsCount: number;
  failedJobsCount: number;
  databaseStatus: string;
  databaseLatencyMs: number;
  uptime: string;
  serverTimeUtc: string;
  environment: string;
}

export interface SystemCostSummaryDto {
  totalAiCostUsd: number;
  totalTtsCostUsd: number;
  totalCostUsd: number;
  totalProjectsCount: number;
  publishedProjectsCount: number;
  averageCostPerProjectUsd: number;
  totalTokensUsed: number;
  totalTtsCharactersSynthesized: number;
  preferredAiProvider: string;
  preferredTtsProvider: string;
}

export interface StorageCleanupReportDto {
  expiredAssetsCleanedCount: number;
  orphanedDirectoriesRemovedCount: number;
  totalBytesFreedMb: number;
  currentStorageUsedMb: number;
  storageLimitMb: number;
  storageWithinLimits: boolean;
  executedAtUtc: string;
}

export interface JobRecoveryReportDto {
  totalDanglingProjectsExamined: number;
  recoveredToAwaitingApprovalCount: number;
  resetToPendingCount: number;
  markedFailedCount: number;
  protectedPublishedProjectsCount: number;
  actionLogs: string[];
  executedAtUtc: string;
}

export interface RenderStatusResponseDto {
  projectId: string;
  status: string;
  progressPercentage: number;
  videoUrl?: string;
  audioUrl?: string;
  subtitleUrl?: string;
  durationSeconds?: number;
  errorMessage?: string;
}

export interface VideoAssetDto {
  id: string;
  videoProjectId: string;
  assetType: string;
  localTempPath: string;
  fileSizeBytes: number;
  mimeType: string;
  sha256Hash?: string;
  expiresAtUtc?: string;
  isCleanedUp: boolean;
  createdAtUtc: string;
}

export interface VideoScriptDto {
  id: string;
  videoProjectId: string;
  title: string;
  hook: string;
  voiceOverScript: string;
  keyFacts: string;
  context: string;
  analysis: string;
  conclusion: string;
  sourceAttribution: string;
  aiProvider: string;
  modelName: string;
  tokenUsage: number;
  estimatedCostUsd?: number;
  status: string;
  wordCount: number;
  estimatedReadingSeconds: number;
}

export interface VideoScenePlanDto {
  id?: string;
  sceneNumber: number;
  sceneIndex?: number;
  startTimeSeconds: number;
  endTimeSeconds: number;
  durationSeconds: number;
  voiceText: string;
  voiceOver?: string;
  subtitle?: string;
  onScreenText: string;
  headline?: string;
  selectedArticleImageId?: string;
  articleImageId?: string;
  imageUrl?: string;
  imageCaption?: string;
  visualType: string;
  motionType: string;
  transitionType: string;
  scenePurpose: string;
  cropType?: string;
  isImageReused?: boolean;
  /** 'WebSearch' = related Creative Commons illustration (the article's own photos could not be fetched) */
  imageSource?: 'Article' | 'WebSearch' | null;
}

export interface ArticleImageDto {
  id: string;
  articleId: string;
  originalImageUrl: string;
  localTempPath?: string;
  caption?: string;
  altText?: string;
  articleImageOrder: number;
  width?: number;
  height?: number;
  imageHash?: string;
  rightsStatus: string;
  category: string;
  /** Extracted | Excluded | Unavailable (could not be downloaded) | WebSearch (related CC illustration) */
  status: string;
  errorMessage?: string | null;
  selectedForVideo: boolean;
}

export interface ArticleDto {
  id: string;
  title: string;
  url: string;
  canonicalUrl: string;
  publisher: string;
  author?: string;
  description?: string;
  contentBody: string;
  publishedAtUtc?: string;
  status: string;
  createdAtUtc: string;
  imageCount: number;
  images: ArticleImageDto[];
}

export interface UpdateArticleImageRequest {
  caption?: string;
  rightsStatus?: string;
  category?: string;
  selectedForVideo?: boolean;
}

export interface QualityCheckItemDto {
  checkCode: string;
  title: string;
  category: string;
  passed: boolean;
  score: number;
  isFatal: boolean;
  details: string;
  recommendation?: string;
}

export interface QualityAssessmentResultDto {
  totalScore: number;
  hasFatalViolations: boolean;
  canPublish: boolean;
  summary: string;
  fatalViolations: string[];
  checkItems: QualityCheckItemDto[];
  evaluatedAtUtc: string;
  notice: string;
}

export interface VideoReviewDto {
  id: string;
  videoProjectId: string;
  reviewedBy: string;
  decision: string; // Pending, Approved, Rejected, NeedsScriptRegen, NeedsAudioRegen, NeedsReRender
  feedbackNotes?: string;
  internalQualityScore: number;
  seriousViolationsFound: boolean;
  violationDetails?: string;
  canPublish: boolean;
  reviewedAtUtc?: string;
  assessment?: QualityAssessmentResultDto;
}

export interface FacebookAccountDto {
  id: string;
  pageId: string;
  pageName: string;
  isActive: boolean;
  hasToken: boolean;
  maskedToken: string;
  tokenExpiresAtUtc?: string;
  status: string;
  createdAtUtc: string;
  instagramUsername?: string | null;
}

export interface ConnectFacebookPageRequestDto {
  pageId: string;
  pageName: string;
  pageAccessToken: string;
  tokenExpiresAtUtc?: string;
}

export interface VerifyTokenResultDto {
  isValid: boolean;
  pageId?: string;
  pageName?: string;
  expiresAtUtc?: string;
  scopes?: string[];
  errorMessage?: string;
}

export interface UserTokenPageDto {
  pageId: string;
  pageName: string;
  alreadyConnected: boolean;
  neverExpires: boolean;
  expiresAtUtc?: string | null;
  connected: boolean;
  instagramUsername?: string | null;
}

export interface UserTokenPagesResultDto {
  pages: UserTokenPageDto[];
  longLived: boolean;
  userTokenExpiresAtUtc?: string | null;
  missingPermissions: string[];
}

export interface PublishReelRequestDto {
  facebookAccountId?: string;
  pageId?: string;
  customCaption?: string;
  autoCleanupAssets?: boolean;
  /** The page's own first comment under the Reel */
  firstComment?: string;
  /** Also post the Reel on the page's linked Instagram */
  postToInstagram?: boolean;
}

export interface PublishReelResultDto {
  facebookPostRecordId: string;
  facebookPostId: string;
  facebookVideoId: string;
  postUrl: string;
  uploadStatus: string;
  publishedAtUtc?: string;
  errorMessage?: string;
  ephemeralAssetsCleanedUp: boolean;
  firstCommentId?: string | null;
  firstCommentError?: string | null;
  instagramMediaId?: string | null;
  instagramPermalink?: string | null;
  instagramError?: string | null;
}

export interface FacebookPostDto {
  id: string;
  videoProjectId: string;
  facebookAccountId?: string;
  pageName?: string;
  facebookPostId?: string;
  facebookVideoId?: string;
  postUrl?: string;
  uploadStatus: string;
  publishedAtUtc?: string;
  status: string;
  errorMessage?: string;
  latestMetric?: VideoMetricDto;
  firstCommentId?: string | null;
  firstCommentError?: string | null;
  instagramPermalink?: string | null;
  instagramError?: string | null;
  instagramViews?: number | null;
  instagramLikes?: number | null;
  instagramComments?: number | null;
}

export interface VideoMetricDto {
  id: string;
  facebookPostId: string;
  views: number;
  reach: number;
  engagement: number;
  shares: number;
  comments: number;
  watchTimeSeconds: number;
  collectedAtUtc: string;
}

// ── Tự động đăng (nhiều Page) ──
export type AutoPostRegionMode = 'Mixed' | 'VN' | 'World';

export interface NewsTopicDto {
  key: string;
  name: string;
  description: string;
}

/** Settings of one Facebook Page. */
export interface AutoPostSettingsDto {
  enabled: boolean;
  /** Topic key: 'tong-hop', 'the-thao', … or 'tu-chon' */
  topic: string;
  /** Comma-separated keywords (required for 'tu-chon') */
  topicKeywords?: string | null;
  postsPerDay: number;
  /** "HH:mm", Vietnam time */
  timeSlots: string[];
  regionMode: AutoPostRegionMode;
  voice?: string | null;
  voiceSpeed?: string | null;
  aiProvider?: string | null;
  targetDurationSeconds: number;
  leadMinutes: number;
  /** Daily "Điểm tin nóng": one wall post with pictures + one Story */
  digestEnabled: boolean;
  /** "HH:mm", Vietnam time */
  digestTime: string;
  /** Stories in the digest (3–9) */
  digestItems: number;
  /** Sundays: "Tuần qua có gì" instead of the daily digest */
  weeklyDigestEnabled: boolean;
  /** Post big breaking news right away */
  breakingEnabled: boolean;
  /** Max breaking-news videos per day (0–5) */
  breakingPerDay: number;
  /** Prefer the kinds of stories that got the most views */
  autoTune: boolean;
  /** Also post every Reel on the page's linked Instagram */
  instagramEnabled: boolean;
  /** neutral | youthful | expert | brief */
  writingTone: string;
  /** A logo was uploaded (read-only) */
  hasLogo?: boolean;
}

export interface EditorialPassDto {
  consistent: boolean;
  issues: string[];
  caption: string;
  firstComment: string;
  checkedAtUtc: string;
}

export interface InsightBucketDto {
  key: string;
  label: string;
  videos: number;
  averageViews: number;
  ratio: number;
}

export interface PageInsightsDto {
  facebookAccountId: string;
  samples: number;
  minSamples: number;
  enoughData: boolean;
  averageViews: number;
  hours: InsightBucketDto[];
  topics: InsightBucketDto[];
  regions: InsightBucketDto[];
  suggestedSlots: string[];
}

export interface DailyDigestItemDto {
  title: string;
  headline: string;
  summary: string;
  sourceName: string;
  url: string;
  imageUrl?: string | null;
  coverageCount: number;
  hotScore: number;
}

export type DailyDigestStatus = 'Working' | 'Published' | 'Partial' | 'Failed' | 'Preview';

export interface DailyDigestRunDto {
  id: string;
  facebookAccountId?: string | null;
  pageName?: string | null;
  digestDate: string;
  kind: 'Daily' | 'Weekly';
  isPreview: boolean;
  status: DailyDigestStatus;
  message?: string | null;
  caption?: string | null;
  items: DailyDigestItemDto[];
  postImages: string[];
  storyImages: string[];
  postId?: string | null;
  postUrl?: string | null;
  postError?: string | null;
  storyPostId?: string | null;
  storyError?: string | null;
  createdAtUtc: string;
  publishedAtUtc?: string | null;
}

export type AutoPostRunStatus = 'Producing' | 'WaitingSlot' | 'Publishing' | 'Published' | 'Failed' | 'Skipped';

export interface AutoPostRunDto {
  id: string;
  slotKey: string;
  facebookAccountId?: string | null;
  pageName?: string | null;
  scheduledAtUtc: string;
  slotTime: string;
  slotDate: string;
  isManual: boolean;
  isBreaking?: boolean;
  status: AutoPostRunStatus;
  attempts: number;
  articleUrl?: string | null;
  articleTitle?: string | null;
  sourceName?: string | null;
  region?: 'VN' | 'World' | null;
  hotScore?: number | null;
  videoProjectId?: string | null;
  projectStatus?: string | null;
  progressPercentage?: number | null;
  facebookPostUrl?: string | null;
  startedAtUtc?: string | null;
  publishedAtUtc?: string | null;
  message?: string | null;
}

export interface AutoPostSlotDto {
  time: string;
  scheduledAtUtc: string;
  plannedRegion: 'VN' | 'World';
  /** 'Upcoming' | 'Missed' | run status */
  state: string;
  run?: AutoPostRunDto | null;
}

export interface AutoPostPageDto {
  facebookAccountId: string;
  pageId: string;
  pageName: string;
  instagramUsername?: string | null;
  tokenStatus: string;
  tokenExpiresAtUtc?: string | null;
  settings: AutoPostSettingsDto;
  topicName: string;
  todaySlots: AutoPostSlotDto[];
  publishedToday: number;
  failedToday: number;
  nextSlotUtc?: string | null;
  currentRun?: AutoPostRunDto | null;
  lastPublished?: AutoPostRunDto | null;
  warnings: string[];
}

export interface AutoPostTotalsDto {
  pages: number;
  pagesEnabled: number;
  plannedToday: number;
  publishedToday: number;
  inProgress: number;
  failedToday: number;
  publishedLast7Days: number;
}

export interface AutoPostOverviewDto {
  today: string;
  pages: AutoPostPageDto[];
  totals: AutoPostTotalsDto;
  recentRuns: AutoPostRunDto[];
  maxConcurrentVideos: number;
  warnings: string[];
}

// ── Lượt xem theo Page ──
export interface PageTopVideoDto {
  videoProjectId?: string | null;
  title: string;
  postUrl?: string | null;
  views: number;
  publishedAtUtc?: string | null;
}

export interface PageViewStatsDto {
  facebookAccountId: string;
  pageName: string;
  topic?: string | null;
  topicName?: string | null;
  isActive: boolean;
  videos: number;
  videosWithStats: number;
  totalViews: number;
  viewsToday: number;
  views7Days: number;
  viewsPrevious7Days: number;
  averageViewsPerVideo: number;
  reach: number;
  likes: number;
  comments: number;
  shares: number;
  /** Views gained per day, aligned to PageStatsOverviewDto.dates */
  dailyViews: number[];
  topVideo?: PageTopVideoDto | null;
  lastSyncedUtc?: string | null;
  syncError?: string | null;
  instagramUsername?: string | null;
  instagramVideos: number;
  instagramViews: number;
  instagramViewsToday: number;
  instagramViews7Days: number;
  instagramLikes: number;
  instagramComments: number;
  instagramDailyViews: number[];
  instagramSyncError?: string | null;
  followers?: number | null;
  followersChange7Days?: number | null;
  instagramFollowers?: number | null;
  instagramFollowersChange7Days?: number | null;
  recentPosts: PagePostStatsDto[];
}

/** A viewer's comment with the reply drafted by AI. */
/** One click on "Mở Facebook – ủng hộ Page" on the public page /cheo-uy-tinh. */
export interface LandingClickDto {
  id: string;
  clickedAtUtc: string;
  device: 'mobile' | 'desktop';
  source?: string | null;
  /** Short anonymous browser id — same id = same person. */
  visitor?: string | null;
  visitorClicks: number;
  handledAtUtc?: string | null;
}

export interface LandingStatsDto {
  views: number;
  uniqueVisitors: number;
  openPageClicks: number;
  postClicks: number;
  mobileViews: number;
  desktopViews: number;
}

export interface PostCommentDto {
  id: string;
  network: 'facebook' | 'instagram' | string;
  pageName?: string | null;
  postTitle: string;
  postUrl?: string | null;
  author?: string | null;
  message: string;
  commentedAtUtc: string;
  status: string;
  suggestedReply?: string | null;
  sentReply?: string | null;
  replyError?: string | null;
}

/** One post's numbers: a news Reel (Tool), a Video AI video (Studio) or a post made by hand (Discovered). */
export interface PagePostStatsDto {
  title: string;
  source: 'Tool' | 'Studio' | 'Discovered' | string;
  publishedAtUtc?: string | null;
  facebookUrl?: string | null;
  instagramUrl?: string | null;
  views?: number | null;
  likes?: number | null;
  comments?: number | null;
  shares?: number | null;
  instagramViews?: number | null;
  instagramLikes?: number | null;
  instagramComments?: number | null;
  /** The page's first comment: "posted", the reason it failed, or null. */
  firstComment?: string | null;
  instagramFirstComment?: string | null;
}

export interface PageStatsOverviewDto {
  days: number;
  /** Vietnam dates (yyyy-MM-dd), oldest first */
  dates: string[];
  dailyViews: number[];
  totals: {
    videos: number;
    totalViews: number;
    viewsToday: number;
    views7Days: number;
    viewsPrevious7Days: number;
    instagramVideos: number;
    instagramViews: number;
    instagramViewsToday: number;
    instagramViews7Days: number;
    followers: number;
    followersChange7Days: number;
    instagramFollowers: number;
    instagramFollowersChange7Days: number;
  };
  pages: PageViewStatsDto[];
  lastSyncUtc?: string | null;
}

export interface PageStatsSyncResultDto {
  postsChecked: number;
  postsUpdated: number;
  errors: string[];
}

// ── Kiểm chứng & nguồn gốc ──
export type GateStatus = 'PENDING' | 'APPROVED' | 'REVIEW_REQUIRED' | 'REJECTED';

export interface FactCheckReportDto {
  gate: { status: GateStatus; reasons: string[]; checkedAtUtc?: string | null };
  factSheet?: {
    entities: { name: string; category: string; kind?: string | null }[];
    claims: { fact: string; quote?: string | null; certainty: string; quoteFound: boolean }[];
    chart?: { title: string; unit?: string | null; source?: string | null; points: { label: string; value: number }[] } | null;
    imageQueries: string[];
    numbersNotInArticle: string[];
    checkedAtUtc?: string | null;
  } | null;
  editorialPass?: EditorialPassDto | null;
  images: {
    imageId: string;
    url?: string | null;
    imageType?: string | null;
    sourceName?: string | null;
    sourcePageUrl?: string | null;
    licenseType?: string | null;
    licenseEvidence?: string | null;
    subjectName?: string | null;
    subjectType?: string | null;
    verificationStatus?: string | null;
    confidence?: number | null;
    caption?: string | null;
    scenes: number[];
  }[];
  articleId?: string | null;
  articleUrl?: string | null;
  articlePublisher?: string | null;
  articlePublishedAtUtc?: string | null;
}

// ── Chi phí AI ──
export interface AiBudgetSettingsDto {
  monthlyUsd: number;
  /** warn | edge | pause */
  mode: string;
}

export interface AiUsageReportDto {
  budget: AiBudgetSettingsDto & { spentUsd: number; overBudget: boolean };
  projectedUsd: number;
  byKind: { kind: string; calls: number; freeCalls: number; costUsd: number }[];
  byDay: { date: string; costUsd: number }[];
  videos: number;
  costPerVideo: number;
}
