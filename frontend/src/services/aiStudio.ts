/* AI Video Studio API client. Every data call carries the studio key (kept only in this browser); media links stay
   plain URLs so <img>/<video> can load them. Videos go straight to the Railway API in 4 MB pieces (a failed piece is
   sent again) because the Vercel proxy drops request bodies of a few MB. */
import { getStudioKey, StudioError } from './studio';

const BASE = '/api/ai-studio';
const CHUNK = 4 * 1024 * 1024;
const RAILWAY_API = 'https://postnewsauto.up.railway.app/api/ai-studio';
const UPLOAD_BASE = /^(localhost|127\.0\.0\.1)$/.test(window.location.hostname) ? BASE : RAILWAY_API;

function headers(extra?: Record<string, string>) {
  const h: Record<string, string> = { ...(extra ?? {}) };
  const key = getStudioKey();
  if (key) h['X-Studio-Key'] = key;
  return h;
}

async function read<T>(res: Response): Promise<T> {
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
      data?.message || data?.title || (res.status >= 500 ? `Server tạm thời không phản hồi (HTTP ${res.status}) — thử lại sau ít phút.` : text || `Lỗi HTTP ${res.status}`);
    throw new StudioError(msg, res.status === 401 && !!data?.needsStudioKey);
  }
  return data as T;
}

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers: headers(body !== undefined ? { 'Content-Type': 'application/json' } : undefined),
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  return read<T>(res);
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

async function sendFile(file: File, onProgress?: (p: number) => void): Promise<string> {
  const { uploadId } = await read<{ uploadId: string }>(await fetch(`${UPLOAD_BASE}/uploads`, { method: 'POST', headers: headers() }));
  onProgress?.(0);
  for (let offset = 0; offset < file.size; offset += CHUNK) {
    const piece = file.slice(offset, offset + CHUNK);
    for (let attempt = 1; ; attempt++) {
      try {
        await read(await fetch(`${UPLOAD_BASE}/uploads/${uploadId}?offset=${offset}`, {
          method: 'PUT',
          headers: headers({ 'Content-Type': 'application/octet-stream' }),
          body: piece,
        }));
        break;
      } catch (e) {
        if (attempt >= 4 || (e instanceof StudioError && e.needsKey)) throw e instanceof StudioError ? e : new StudioError('Mất kết nối khi tải file lên — thử lại.');
        await sleep(1500 * attempt);
      }
    }
    onProgress?.(Math.min(1, (offset + piece.size) / file.size));
  }
  return uploadId;
}

async function upload<T>(path: string, file: File, fields: Record<string, string | undefined | null>, onProgress?: (p: number) => void): Promise<T> {
  const uploadId = await sendFile(file, onProgress);
  const form = new FormData();
  form.append('uploadId', uploadId);
  form.append('fileName', file.name);
  form.append('contentType', file.type || '');
  Object.entries(fields).forEach(([k, v]) => v != null && v !== '' && form.append(k, String(v)));
  return read<T>(await fetch(`${UPLOAD_BASE}${path}`, { method: 'POST', headers: headers(), body: form }));
}

// ───────── types ─────────

export type ProjectStatus = 'Draft' | 'Queued' | 'Analyzing' | 'Analyzed' | 'Failed' | 'Cancelled';
export type GenStatus = 'Queued' | 'Generating' | 'Recovering' | 'Done' | 'Failed' | 'Cancelled';
export type PromptMode = 'Faithful' | 'Creative' | 'Cinematic';
export type Json = any;

export interface Budget {
  monthSpentUsd: number;
  monthlyBudgetUsd: number;
  monthRemainingUsd: number;
}

export interface VideoModel {
  id: string;
  label: string;
  note: string;
  pricePerSecondUsd: Record<string, number>;
}

export interface EngineStatus {
  ready: boolean;
  error?: string | null;
  checkedAtUtc?: string | null;
  machine?: string | null;
  home?: string | null;
  python?: string | null;
  openCv?: string | null;
  backends: Record<string, boolean>;
  models: Record<string, boolean>;
  whisper: boolean;
  cpuThreads: number;
  ramGb: number;
  freeDiskGb?: number | null;
  tokensPerSecond: Record<string, number>;
  benchBackend: Record<string, string>;
}

export interface NarratorVoice {
  id: string;
  name: string;
  gender: string;
  description: string;
  /** Designed for the channel in the studio's "Giọng đọc" tab. */
  designed?: boolean;
}

export type AudioMode = 'None' | 'Narration' | 'Original';

export interface Overview {
  pcOnline: boolean;
  pcUpdating: boolean;
  pcMachine?: string | null;
  engine: EngineStatus;
  seedanceConfigured: boolean;
  videoModels: VideoModel[];
  topics: { id: string; label: string; description: string }[];
  voices: NarratorVoice[];
  pages: { id: string; name: string; hasLogo: boolean }[];
  resolutions: string[];
  minClipSeconds: number;
  maxClipSeconds: number;
  budget: Budget;
  projects: number;
  analyzing: number;
  generating: number;
  spentUsd: number;
  storageMb: number;
}

export interface Settings {
  defaultModel: '4b' | '8b';
  threads: number;
  gpu: 'auto' | 'cpu';
  maxFramesPerScene: number;
  transcribe: boolean;
  workDir?: string | null;
  maxRamMb: number;
  sourceRetentionDays: number;
  clipRetentionDays: number;
}

export interface ProjectSummary {
  id: string;
  title: string;
  status: ProjectStatus;
  durationSeconds: number;
  width: number;
  height: number;
  aspectRatio: string;
  scenesTotal: number;
  progressPercent: number;
  progressText?: string | null;
  hasThumb: boolean;
  clipsDone: number;
  costUsd: number;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface Frame {
  t: number;
  reason: string;
  blobId?: string | null;
}

export interface Generation {
  id: string;
  projectId: string;
  projectTitle?: string | null;
  sceneId: string;
  sceneOrder?: number | null;
  batchId: string;
  status: GenStatus;
  model: string;
  resolution: string;
  aspectRatio: string;
  seconds: number;
  seed?: number | null;
  prompt: string;
  imageSource?: string | null;
  estimatedCostUsd: number;
  costUsd: number;
  hasClip: boolean;
  paid: boolean;
  errorMessage?: string | null;
  attempts: number;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  finishedAtUtc?: string | null;
  filesDeleted: boolean;
}

export interface Scene {
  id: string;
  index: number;
  order: number;
  startSeconds: number;
  endSeconds: number;
  isContinuation: boolean;
  enabled: boolean;
  targetSeconds: number;
  hasThumb: boolean;
  frames: Frame[];
  measured?: Json;
  analysis?: Json;
  confidence: number;
  summaryVi?: string | null;
  transcript?: string | null;
  narration?: string | null;
  narrationEn?: string | null;
  narrationEdited: boolean;
  prompt: string;
  promptVersion: number;
  promptEdited: boolean;
  estimatedCostUsd: number;
  latest?: Generation | null;
}

export interface Variation {
  character?: string | null;
  wardrobe?: string | null;
  setting?: string | null;
  camera?: string | null;
  lighting?: string | null;
  style?: string | null;
}

export interface Project extends ProjectSummary {
  sourceFileName: string;
  sourceContainer: string;
  sourceSizeBytes: number;
  fpsText: string;
  fps: number;
  videoCodec?: string | null;
  bitrateKbps?: number | null;
  hasAudio: boolean;
  audioTracks: { codec?: string | null; sampleRate: number; channels: number }[];
  hasSource: boolean;
  sourceDeleted: boolean;
  analysisModel: '4b' | '8b';
  stage?: string | null;
  scenesDone: number;
  cancelRequested: boolean;
  workerMachine?: string | null;
  analysisStartedAtUtc?: string | null;
  analysisFinishedAtUtc?: string | null;
  errorMessage?: string | null;
  analysisMeta?: Json;
  overview?: Json;
  characters?: Json;
  environments?: Json;
  style?: Json;
  palette: string[];
  transcript?: Json;
  promptMode: PromptMode;
  topic: string;
  audioMode: AudioMode;
  hasSourceAudio: boolean;
  narratorVoice: string;
  narratorStyle: string;
  /** The Page the video is for: its name / logo is the moving watermark of the joined video (against re-uploads). */
  facebookAccountId?: string | null;
  watermark: boolean;
  /** Facebook caption (editable; the AI disclosure line is added on copy). */
  caption: string;
  /** The whole narration read in one continuous take. */
  hasNarrationTake: boolean;
  narrationCurrent: boolean;
  narrationSeconds?: number | null;
  speakerProfileVi?: string | null;
  variation: Variation;
  globalPrompt: string;
  negativePrompt: string;
  videoModel: string;
  resolution: string;
  genAspectRatio: string;
  seed?: number | null;
  continuity: boolean;
  hasReferenceImage: boolean;
  hasFinal: boolean;
  finalStatus?: string | null;
  finalError?: string | null;
  finalUpdatedAtUtc?: string | null;
  scenes: Scene[];
}

export interface PromptVersion {
  id: string;
  version: number;
  text: string;
  source: 'engine' | 'user' | 'restore';
  mode: string;
  createdAtUtc: string;
}

export interface Estimate {
  lines: { sceneId: string; order: number; seconds: number; costUsd: number; skip?: string | null }[];
  totalUsd: number;
  totalSeconds: number;
  budget: Budget;
  blocker?: string | null;
}

export interface GeneratorSettings {
  videoModel: string;
  resolution: string;
  aspectRatio: string;
  seed?: number | null;
  continuity: boolean;
  audioMode?: AudioMode;
}

// ───────── calls ─────────

const newKey = () => (crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`);

export const aiStudio = {
  overview: () => call<Overview>('GET', '/overview'),
  settings: () => call<Settings>('GET', '/settings'),
  saveSettings: (s: Settings) => call<Settings>('PUT', '/settings', s),
  checkEngine: (bench: boolean) => call<{ queued: boolean }>('POST', '/engine/check', { bench }),

  projects: () => call<ProjectSummary[]>('GET', '/projects'),
  project: (id: string) => call<Project>('GET', `/projects/${id}`),
  createProject: (file: File, title: string, topic: string, onProgress?: (p: number) => void) => upload<Project>('/projects', file, { title, topic }, onProgress),
  replaceSource: (id: string, file: File, onProgress?: (p: number) => void) => upload<Project>(`/projects/${id}/source`, file, {}, onProgress),
  updateProject: (id: string, body: { title?: string; analysisModel?: string }) => call<Project>('PUT', `/projects/${id}`, body),
  deleteProject: (id: string) => call<void>('DELETE', `/projects/${id}`),
  analyze: (id: string, force: boolean) => call<Project>('POST', `/projects/${id}/analyze`, { force }),
  cancelAnalysis: (id: string) => call<Project>('POST', `/projects/${id}/cancel`),

  promptSettings: (id: string, body: { mode: PromptMode; topic?: string; variation: Variation; globalPrompt?: string; negativePrompt?: string; overwriteEdited: boolean }) =>
    call<Project>('PUT', `/projects/${id}/prompt-settings`, body),
  bibles: (id: string, body: { characters?: Json; environments?: Json; style?: Json; overwriteEdited: boolean }) =>
    call<Project>('PUT', `/projects/${id}/bibles`, body),
  reorder: (id: string, sceneIds: string[]) => call<Project>('PUT', `/projects/${id}/order`, sceneIds),
  updateScene: (id: string, sceneId: string, body: { enabled?: boolean; targetSeconds?: number }) => call<Scene>('PUT', `/projects/${id}/scenes/${sceneId}`, body),
  editPrompt: (id: string, sceneId: string, text: string) => call<Scene>('PUT', `/projects/${id}/scenes/${sceneId}/prompt`, { text }),
  regeneratePrompt: (id: string, sceneId: string) => call<Scene>('POST', `/projects/${id}/scenes/${sceneId}/regenerate-prompt`),
  narrator: (id: string, body: { voice?: string; style?: string; suggest?: boolean }) => call<Project>('PUT', `/projects/${id}/narrator`, body),
  resetNarration: (id: string) => call<Project>('POST', `/projects/${id}/narration/reset`),
  editNarration: (id: string, sceneId: string, text: string) => call<Scene>('PUT', `/projects/${id}/scenes/${sceneId}/narration`, { text }),
  speakNarration: (id: string) => call<Project>('POST', `/projects/${id}/narration/speak`),
  brand: (id: string, body: { facebookAccountId: string | null; watermark: boolean }) => call<Project>('PUT', `/projects/${id}/brand`, body),
  setCaption: (id: string, text: string) => call<Project>('PUT', `/projects/${id}/caption`, { text }),
  writeCaption: (id: string) => call<Project>('POST', `/projects/${id}/caption/write`),
  versions: (id: string, sceneId?: string) => call<PromptVersion[]>('GET', `/projects/${id}/versions${sceneId ? `?sceneId=${sceneId}` : ''}`),
  restore: (id: string, versionId: string) => call<Project>('POST', `/projects/${id}/versions/restore`, { versionId }),

  generator: (id: string, s: GeneratorSettings) => call<Project>('PUT', `/projects/${id}/generator`, s),
  setReferenceImage: (id: string, file: File) => upload<Project>(`/projects/${id}/reference-image`, file, {}),
  removeReferenceImage: (id: string) => call<Project>('DELETE', `/projects/${id}/reference-image`),
  estimate: (id: string, sceneIds: string[]) => call<Estimate>('POST', `/projects/${id}/estimate`, { sceneIds }),
  generate: (id: string, sceneIds: string[], confirmedCostUsd: number) =>
    call<Project>('POST', `/projects/${id}/generate`, { sceneIds, confirmedCostUsd, requestKey: newKey() }),
  assemble: (id: string) => call<Project>('POST', `/projects/${id}/assemble`),
  generations: (projectId?: string) => call<Generation[]>('GET', `/generations${projectId ? `?projectId=${projectId}` : ''}`),
  recover: (genId: string) => call<Generation>('POST', `/generations/${genId}/recover`),
  cancelGeneration: (genId: string) => call<Generation>('POST', `/generations/${genId}/cancel`),

  sourceUrl: (id: string) => `${BASE}/projects/${id}/source`,
  thumbUrl: (id: string, v?: string | null) => `${BASE}/projects/${id}/thumb${v ? `?v=${encodeURIComponent(v)}` : ''}`,
  referenceImageUrl: (id: string, v?: string | null) => `${BASE}/projects/${id}/reference-image${v ? `?v=${encodeURIComponent(v)}` : ''}`,
  finalUrl: (id: string, download = false, v?: string | null) => `${BASE}/projects/${id}/final?${download ? 'download=true&' : ''}${v ? `v=${encodeURIComponent(v)}` : ''}`,
  sceneThumbUrl: (sceneId: string) => `${BASE}/scenes/${sceneId}/thumb`,
  frameUrl: (sceneId: string, blobId: string) => `${BASE}/scenes/${sceneId}/frames/${blobId}`,
  narrationUrl: (id: string, v?: number | null) => `${BASE}/projects/${id}/narration${v ? `?v=${v}` : ''}`,
  clipUrl: (genId: string, download = false) => `${BASE}/generations/${genId}/clip${download ? '?download=true' : ''}`,
};

export const isAnalyzing = (s: ProjectStatus) => s === 'Queued' || s === 'Analyzing';
export const isGenActive = (s?: GenStatus | null) => s === 'Queued' || s === 'Generating' || s === 'Recovering';
