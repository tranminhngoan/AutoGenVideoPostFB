/* AI video studio API client (Seedance text-to-video + TTS). Writes need the X-Studio-Key header (the server's Vercel AI Gateway or Gemini key), asked once and kept
   only in this browser's localStorage. Media URLs (images, music, clips, final video) need no key. */

const BASE = '/api/studio';
const KEY_STORAGE = 'studio.accessKey';

export function getStudioKey(): string {
  try {
    return localStorage.getItem(KEY_STORAGE) || '';
  } catch {
    return '';
  }
}

export function setStudioKey(key: string | null) {
  try {
    if (key) localStorage.setItem(KEY_STORAGE, key.trim());
    else localStorage.removeItem(KEY_STORAGE);
  } catch {
    /* private mode: just not remembered */
  }
}

export class StudioError extends Error {
  needsKey: boolean;
  constructor(message: string, needsKey = false) {
    super(message);
    this.needsKey = needsKey;
  }
}

async function call<T>(method: string, path: string, body?: unknown, isForm = false): Promise<T> {
  const headers: Record<string, string> = {};
  const key = getStudioKey();
  if (key) headers['X-Studio-Key'] = key;
  if (body !== undefined && !isForm) headers['Content-Type'] = 'application/json';
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : isForm ? (body as FormData) : JSON.stringify(body),
  });
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  let data: any = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = null;
  }
  if (!res.ok) {
    const msg = data?.message || data?.title || text || `Lỗi HTTP ${res.status}`;
    throw new StudioError(msg, res.status === 401 && !!data?.needsStudioKey);
  }
  return data as T;
}

// ───────── types ─────────

export interface StudioMusic {
  id: string;
  name: string;
  mood: string;
  durationSeconds: number;
  sizeBytes: number;
  useCount: number;
  createdAtUtc: string;
}

export type VideoStatus = 'Draft' | 'Planning' | 'Planned' | 'Generating' | 'Composing' | 'Ready' | 'Publishing' | 'Published' | 'Failed';

export interface StudioShot {
  id: string;
  index: number;
  durationSeconds: number;
  action: string;
  camera: string;
  location?: string | null;
  cast: string[];
  speaker?: string | null;
  dialogue?: string | null;
  sound?: string | null;
  hasVoice: boolean;
  voiceSeconds?: number | null;
  hasSfx: boolean;
  /** Scene of a library script: its narration is read in one take running over the scene's shots. */
  scene?: number | null;
  /** How the scene's narration is performed (English direction for the voice model). */
  voiceStyle?: string | null;
  /** This shot's words are part of the scene take held by shot number N. */
  voiceFromShot?: number | null;
  status: 'Pending' | 'Generating' | 'Done' | 'Failed';
  hasClip: boolean;
  costUsd: number;
  estimatedCostUsd: number;
  attempts: number;
  errorMessage?: string | null;
}

export interface StudioVideoSummary {
  id: string;
  title: string;
  facebookAccountId?: string | null;
  pageName?: string | null;
  targetDurationSeconds: number;
  status: VideoStatus;
  progress?: string | null;
  shotCount: number;
  estimatedCostUsd: number;
  actualCostUsd: number;
  hasFinal: boolean;
  createdAtUtc: string;
  publishedAtUtc?: string | null;
}

export interface StudioCharacter {
  name: string;
  gender: 'male' | 'female' | string;
  look: string;
  /** Worn in every shot (English, headwear stated). */
  outfit?: string | null;
  voice: string;
  speed?: string | null;
}

export interface StudioSceneImport {
  title?: string;
  visualVi?: string;
  promptEn?: string;
  voiceover?: string;
  sound?: string;
  camera?: string;
  durationSeconds: number;
}

/** A finished library script, imported shot for shot (no AI planning), with the video options chosen before creating. */
export interface StudioMusicBrief {
  id: string;
  name: string;
  mood: string;
}

export interface StudioMusicAi {
  configured: boolean;
  model: string;
  pricePerSongUsd: number;
  briefs: StudioMusicBrief[];
  running: boolean;
  total: number;
  done: number;
  added: string[];
  errors: string[];
}

export interface StudioScriptImport {
  title: string;
  code?: string;
  synopsis?: string;
  mood?: string;
  region?: string;
  era?: string;
  hook?: string;
  ending?: string;
  script?: string;
  scenes: StudioSceneImport[];
  facebookAccountId?: string | null;
  videoModel?: string;
  resolution?: string;
  aspectRatio?: string;
  styleNotes?: string;
  narratorVoice?: string;
  narratorSpeed?: string;
  musicMode?: string;
  musicTrackId?: string | null;
  musicVolume?: number;
  sfxEnabled?: boolean;
  sfxVolume?: number;
  autoGenerate?: boolean;
  /** Shorten the narration to about this many seconds (null = the script's own length). */
  targetSeconds?: number | null;
  /** End the narration with an invitation to follow the page. */
  outro?: boolean;
}

export interface StudioLocation {
  name: string;
  look: string;
}

export interface StudioVideo extends StudioVideoSummary {
  script: string;
  videoModel: string;
  aspectRatio: '9:16' | '16:9';
  resolution: '480p' | '720p' | '1080p';
  styleNotes?: string | null;
  characters: StudioCharacter[];
  locations: StudioLocation[];
  sfxEnabled: boolean;
  sfxVolume: number;
  musicMode: 'Auto' | 'Manual' | 'None';
  musicTrackId?: string | null;
  musicName?: string | null;
  musicVolume: number;
  planNotes?: string | null;
  caption?: string | null;
  finalDurationSeconds?: number | null;
  /** Changes with every new final file. */
  finalVersion?: string | null;
  errorMessage?: string | null;
  publishedPostId?: string | null;
  instagramPermalink?: string | null;
  instagramError?: string | null;
  /** The video sent to the page as a draft Reel (to add Facebook music by hand). */
  facebookDraftId?: string | null;
  /** Numbers of the post once published through the app. */
  postStats?: {
    facebookUrl?: string | null;
    instagramUrl?: string | null;
    views?: number | null;
    likes?: number | null;
    comments?: number | null;
    shares?: number | null;
    instagramViews?: number | null;
    instagramLikes?: number | null;
    instagramComments?: number | null;
  } | null;
  shots: StudioShot[];
}

export interface StudioVideoInput {
  title?: string;
  facebookAccountId?: string | null;
  script?: string;
  targetDurationSeconds?: number;
  videoModel?: string;
  aspectRatio?: string;
  resolution?: string;
  styleNotes?: string;
  characters?: StudioCharacter[];
  locations?: StudioLocation[];
  sfxEnabled?: boolean;
  sfxVolume?: number;
  musicMode?: string;
  musicTrackId?: string | null;
  musicVolume?: number;
  caption?: string;
}

export interface StudioShotInput {
  id?: string;
  durationSeconds: number;
  action: string;
  camera: string;
  location?: string | null;
  cast: string[];
  speaker?: string | null;
  dialogue?: string | null;
  sound?: string | null;
  scene?: number | null;
  voiceStyle?: string | null;
}

export interface StudioCosts {
  monthSpentUsd: number;
  monthlyBudgetUsd: number;
  monthRemainingUsd: number;
  allTimeSpentUsd: number;
  monthVideoSeconds: number;
  models: StudioModel[];
  videos: { id: string; title: string; costUsd: number; createdAtUtc: string }[];
  recent: { createdAtUtc: string; kind: string; model: string; seconds?: number | null; costUsd: number; note?: string | null; videoTitle?: string | null }[];
}

export interface StudioModel {
  id: string;
  label: string;
  note: string;
  pricePerSecondUsd: Record<string, number>;
}

export interface StudioVoiceOption {
  id: string;
  name: string;
  gender: 'male' | 'female' | string;
  description: string;
  /** Designed for this channel (e.g. a Southern accent). */
  designed?: boolean;
}

export interface StudioSettings {
  monthlyBudgetUsd: number;
  videoConfigured: boolean;
  videoModel: string;
  models: StudioModel[];
  voices: StudioVoiceOption[];
  voicePricePerSecondUsd: number;
  sfxConfigured: boolean;
  sfxPricePerSecondUsd: number;
  /** The designed voice library scripts start with. */
  defaultNarratorVoice?: string | null;
}

export interface StudioPage {
  id: string;
  /** The page's id on Facebook (opens its composer). */
  pageId?: string;
  pageName: string;
  isActive: boolean;
}

// ───────── calls ─────────

const empty = (id?: string | null) => (id ? id : '00000000-0000-0000-0000-000000000000');

export const studio = {
  checkAccess: () => call<{ ok: boolean }>('GET', '/access'),
  settings: () => call<StudioSettings>('GET', '/settings'),
  designVoice: (input: { name: string; gender: string; description: string; makeDefault: boolean }) =>
    call<StudioSettings>('POST', '/voices/design', input),
  deleteDesignedVoice: (voice: string) => call<StudioSettings>('DELETE', `/voices/design/${encodeURIComponent(voice.replace('gemini:', ''))}`),
  setDefaultNarrator: (voice: string | null) => call<StudioSettings>('PUT', '/voices/default', { voice }),
  setBudget: (monthlyBudgetUsd: number) => call<StudioSettings>('PUT', '/settings/budget', { monthlyBudgetUsd }),
  pages: () => call<StudioPage[]>('GET', '/pages'),

  music: () => call<StudioMusic[]>('GET', '/music'),
  musicAi: () => call<StudioMusicAi>('GET', '/music/ai'),
  composeMusic: (input: { briefIds: string[]; wish?: string }) => call<StudioMusicAi>('POST', '/music/ai', input),
  addMusic: (file: File, name: string, mood: string) => {
    const f = new FormData();
    f.append('file', file);
    f.append('name', name);
    f.append('mood', mood);
    return call<StudioMusic>('POST', '/music', f, true);
  },
  updateMusic: (id: string, patch: { name?: string; mood?: string }) => call<StudioMusic>('PUT', `/music/${id}`, patch),
  deleteMusic: (id: string) => call<void>('DELETE', `/music/${id}`),
  musicAudioUrl: (id: string) => `${BASE}/music/${id}/audio`,

  videos: () => call<StudioVideoSummary[]>('GET', '/videos'),
  video: (id: string) => call<StudioVideo>('GET', `/videos/${id}`),
  createVideo: (input: StudioVideoInput) => call<StudioVideo>('POST', '/videos', { ...input, facebookAccountId: input.facebookAccountId ?? undefined }),
  updateVideo: (id: string, input: StudioVideoInput) =>
    call<StudioVideo>('PUT', `/videos/${id}`, {
      ...input,
      facebookAccountId: input.facebookAccountId === undefined ? undefined : empty(input.facebookAccountId),
      musicTrackId: input.musicTrackId === undefined ? undefined : empty(input.musicTrackId),
    }),
  deleteVideo: (id: string) => call<void>('DELETE', `/videos/${id}`),
  importScript: (input: StudioScriptImport) =>
    call<StudioVideo>('POST', '/videos/import', {
      ...input,
      facebookAccountId: input.facebookAccountId || undefined,
      musicTrackId: input.musicTrackId || undefined,
    }),
  plan: (id: string) => call<StudioVideo>('POST', `/videos/${id}/plan`),
  condense: (id: string, targetSeconds: number) => call<StudioVideo>('POST', `/videos/${id}/condense`, { targetSeconds }),
  saveShots: (id: string, shots: StudioShotInput[]) => call<StudioVideo>('PUT', `/videos/${id}/shots`, shots),
  generate: (id: string) => call<StudioVideo>('POST', `/videos/${id}/generate`),
  generateShot: (id: string, shotId: string) => call<StudioVideo>('POST', `/videos/${id}/shots/${shotId}/generate`),
  voices: (id: string) => call<StudioVideo>('POST', `/videos/${id}/voices`),
  shotVoice: (id: string, shotId: string) => call<StudioVideo>('POST', `/videos/${id}/shots/${shotId}/voice`),
  effects: (id: string) => call<StudioVideo>('POST', `/videos/${id}/effects`),
  shotEffects: (id: string, shotId: string) => call<StudioVideo>('POST', `/videos/${id}/shots/${shotId}/effects`),
  compose: (id: string) => call<StudioVideo>('POST', `/videos/${id}/compose`),
  recoverClips: (id: string) => call<StudioVideo>('POST', `/videos/${id}/recover-clips`),
  publish: (id: string, caption: string) => call<StudioVideo>('POST', `/videos/${id}/publish`, { caption }),
  instagram: (id: string) => call<StudioVideo>('POST', `/videos/${id}/instagram`),
  markPublished: (id: string) => call<StudioVideo>('POST', `/videos/${id}/mark-published`),
  facebookDraft: (id: string, caption: string) => call<StudioVideo>('POST', `/videos/${id}/facebook-draft`, { caption }),
  purgeClips: (id: string) => call<StudioVideo>('POST', `/videos/${id}/purge-clips`),
  finalUrl: (id: string, download = false) => `${BASE}/videos/${id}/final${download ? '?download=true' : ''}`,
  clipUrl: (shotId: string) => `${BASE}/shots/${shotId}/clip`,
  voiceUrl: (shotId: string) => `${BASE}/shots/${shotId}/voice`,
  effectsUrl: (shotId: string) => `${BASE}/shots/${shotId}/effects`,
  voicePreviewUrl: (voice: string) =>
    voice.startsWith('gemini:voice_')
      ? `${BASE}/voices/design/${encodeURIComponent(voice.replace('gemini:', ''))}/sample`
      : `/api/system/voice-preview?voice=${encodeURIComponent(voice)}`,

  costs: () => call<StudioCosts>('GET', '/costs'),
};

export const usd = (v: number | null | undefined, digits = 2) => (v == null ? '—' : `$${v.toFixed(digits)}`);

export const STATUS_LABEL: Record<VideoStatus, string> = {
  Draft: 'Chưa lập kế hoạch',
  Planning: 'Đang lập kế hoạch',
  Planned: 'Đã có kế hoạch',
  Generating: 'Đang tạo clip',
  Composing: 'Đang ghép video',
  Ready: 'Sẵn sàng đăng',
  Publishing: 'Đang đăng',
  Published: 'Đã đăng',
  Failed: 'Lỗi',
};

export const BUSY: VideoStatus[] = ['Planning', 'Generating', 'Composing', 'Publishing'];
