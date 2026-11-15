import { SystemConfig, VideoProject, VideoProjectStatus } from '../types';

const API_BASE = '/api';

export interface CreateProjectRequest {
  title: string;
  sourceUrl: string;
  publisher?: string;
  targetDurationSeconds?: number;
  aiProvider?: string;
  voice?: string;
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

export const api = {
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

  async getSystemInfo() {
    try {
      const res = await fetch(`${API_BASE}/system/info`);
      if (!res.ok) throw new Error('System info failed');
      return await res.json();
    } catch (e) {
      return { appName: 'AI News Studio', version: '1.0.0', dotnetVersion: '8.0' };
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Failed to create video project');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Failed to enqueue processing job');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Trích xuất bài báo thất bại');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Cập nhật thông tin ảnh thất bại');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Tạo kịch bản AI thất bại');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Cập nhật kịch bản thất bại');
    }
    return await res.json();
  },

  async planScenes(projectId: string): Promise<VideoScenePlanDto[]> {
    const res = await fetch(`${API_BASE}/video-projects/${projectId}/scenes/plan`, {
      method: 'POST'
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Phân cảnh kịch bản thất bại');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Cập nhật phân cảnh thất bại');
    }
    return await res.json();
  },

  // Phase 5 Video Rendering & Ephemeral Media APIs
  async renderVideo(projectId: string, ttsProvider?: string): Promise<RenderStatusResponseDto> {
    const url = `${API_BASE}/video-projects/${projectId}/render${ttsProvider ? `?ttsProvider=${ttsProvider}` : ''}`;
    const res = await fetch(url, { method: 'POST' });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.error || 'Yêu cầu render video thất bại');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || 'Kiểm định chất lượng video thất bại');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || 'Không thể phê duyệt video do lỗi nghiêm trọng');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || 'Gửi từ chối video thất bại');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || `Yêu cầu thực hiện ${action} thất bại`);
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || 'Cập nhật tiêu đề & caption thất bại');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || 'Kết nối Facebook Page thất bại');
    }
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

  async disconnectFacebookPage(id: string): Promise<boolean> {
    try {
      const res = await fetch(`${API_BASE}/facebook/pages/${id}`, { method: 'DELETE' });
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || 'Xuất bản Facebook Reels thất bại');
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
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || 'Cập nhật số liệu Insights thất bại');
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
  }
};

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
  startTimeSeconds: number;
  endTimeSeconds: number;
  durationSeconds: number;
  voiceText: string;
  onScreenText: string;
  selectedArticleImageId?: string;
  imageUrl?: string;
  imageCaption?: string;
  visualType: string;
  motionType: string;
  transitionType: string;
  scenePurpose: string;
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
  status: string;
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

export interface PublishReelRequestDto {
  facebookAccountId?: string;
  pageId?: string;
  customCaption?: string;
  autoCleanupAssets?: boolean;
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
