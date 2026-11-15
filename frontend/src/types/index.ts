export type VideoProjectStatus =
  | 'Pending'
  | 'Extracting'
  | 'Extracted'
  | 'Analyzing'
  | 'Scripting'
  | 'PlanningScenes'
  | 'GeneratingAudio'
  | 'Rendering'
  | 'QualityChecking'
  | 'AwaitingApproval'
  | 'Publishing'
  | 'Published'
  | 'Failed'
  | 'Cancelled';

export type RightsStatus = 'Unknown' | 'PermissionRequired' | 'Licensed' | 'Authorized' | 'Rejected';

export interface ArticleImage {
  id: string;
  originalImageUrl: string;
  caption: string;
  altText?: string;
  order: number;
  width?: number;
  height?: number;
  rightsStatus: RightsStatus;
  category?: 'Panorama' | 'Character' | 'Event' | 'Document' | 'Chart' | 'Unsuitable';
  selectedForVideo: boolean;
}

export interface Article {
  id: string;
  title: string;
  url: string;
  publisher: string;
  author?: string;
  publishedDate?: string;
  bodySnippet: string;
  imageCount: number;
  images: ArticleImage[];
  extractedAt: string;
}

export interface VideoScene {
  sceneNumber: number;
  durationSeconds: number;
  voiceText: string;
  onScreenText: string;
  selectedArticleImageId?: string;
  imageUrl?: string;
  motionType: 'ZoomIn' | 'ZoomOut' | 'PanLeft' | 'PanRight' | 'KenBurns';
  transitionType: 'Fade' | 'Slide' | 'Cut';
  scenePurpose: 'Hook' | 'Fact' | 'Context' | 'Analysis' | 'Conclusion';
}

export interface VideoProject {
  id: string;
  title: string;
  sourceUrl: string;
  publisher: string;
  status: VideoProjectStatus;
  durationSeconds: number;
  sceneCount: number;
  scenes: VideoScene[];
  script?: string;
  audioDurationSeconds?: number;
  facebookPostId?: string;
  facebookVideoId?: string;
  createdAt: string;
  updatedAt: string;
}

export interface SystemConfig {
  environment: string;
  ai: {
    provider: 'Gemini' | 'Claude';
    isConfigured: boolean;
    model: string;
  };
  tts: {
    provider: string;
    voice: string;
    isConfigured: boolean;
  };
  facebook: {
    isConfigured: boolean;
    pageId: string;
  };
  storage: {
    tempPath: string;
    maxConcurrentRenders: number;
    maxStorageMb: number;
    ttlMinutes: number;
  };
}

export interface JobHistoryItem {
  id: string;
  projectId: string;
  projectTitle: string;
  status: VideoProjectStatus;
  startedAt: string;
  completedAt?: string;
  durationMs: number;
  retryCount: number;
  lastMessage: string;
}
