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

export interface VideoScene {
  id?: string;
  sceneNumber: number;
  startTimeSeconds?: number;
  endTimeSeconds?: number;
  durationSeconds?: number;
  voiceText: string;
  onScreenText: string;
  imageUrl?: string | null;
  motionType: string;
  transitionType: string;
  scenePurpose: string;
}

export interface ProjectJob {
  id: string;
  videoProjectId: string;
  hangfireJobId?: string | null;
  step: string;
  status: string;
  retryCount: number;
  progressPercentage: number;
  startedAtUtc?: string | null;
  finishedAtUtc?: string | null;
  errorMessage?: string | null;
}

/** Mirrors VideoProjectResponseDto on the API. */
export interface VideoProject {
  id: string;
  title: string;
  sourceUrl: string;
  publisher: string;
  status: VideoProjectStatus | string;
  targetDurationSeconds?: number;
  actualDurationSeconds?: number | null;
  resolution?: string;
  aspectRatio?: string;
  createdAtUtc?: string;
  updatedAtUtc?: string | null;
  errorMessage?: string | null;
  /** Choices saved when the project was created (null for older projects). */
  aiProvider?: string | null;
  voice?: string | null;
  voiceSpeed?: string | null;
  /** Facebook Page the video is made for (name shown on the video) */
  facebookAccountId?: string | null;
  pageName?: string | null;
  /** Topic of that page — sets the video colours */
  topic?: string | null;
  sceneCount: number;
  scenes: VideoScene[];
  jobs?: ProjectJob[];
  /** Publication gate: PENDING, APPROVED, REVIEW_REQUIRED, REJECTED */
  gateStatus?: 'PENDING' | 'APPROVED' | 'REVIEW_REQUIRED' | 'REJECTED';
}

export interface SystemConfig {
  environment: string;
  /** True when the server may return simulated AI/TTS/render/Facebook output (test setups only). */
  simulation?: boolean;
  ai: {
    provider: 'Gemini' | 'Claude' | string;
    isConfigured: boolean;
    model: string;
    /** hasFreeKey: key from a project without billing, tried first; hasPaidKey: billed key used when the free quota runs out. */
    gemini?: { isConfigured: boolean; model: string; hasFreeKey?: boolean; hasPaidKey?: boolean };
    claude?: { isConfigured: boolean; model: string };
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
