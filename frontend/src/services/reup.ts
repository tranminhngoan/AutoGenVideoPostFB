/* Reup Video API client — free processing on the owner's PC. Writes need the same X-Studio-Key as the AI video studio
   (kept only in this browser). Big uploads go through XMLHttpRequest to show their progress. */
import { getStudioKey, StudioError } from './studio';

const BASE = '/api/reup';

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {};
  const key = getStudioKey();
  if (key) headers['X-Studio-Key'] = key;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(`${BASE}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  let data: any = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = null;
  }
  if (!res.ok) throw new StudioError(data?.message || data?.title || text || `Lỗi HTTP ${res.status}`, res.status === 401 && !!data?.needsStudioKey);
  return data as T;
}

/** multipart upload with progress (0–1) */
function upload<T>(path: string, form: FormData, onProgress?: (p: number) => void): Promise<T> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', `${BASE}${path}`);
    const key = getStudioKey();
    if (key) xhr.setRequestHeader('X-Studio-Key', key);
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress?.(e.loaded / e.total);
    xhr.onerror = () => reject(new StudioError('Mất kết nối khi tải file lên.'));
    xhr.onload = () => {
      let data: any = null;
      try {
        data = xhr.responseText ? JSON.parse(xhr.responseText) : null;
      } catch {
        data = null;
      }
      if (xhr.status >= 200 && xhr.status < 300) resolve(data as T);
      else if (xhr.status === 413) reject(new StudioError('File quá lớn so với giới hạn của server.'));
      else reject(new StudioError(data?.message || `Lỗi HTTP ${xhr.status}`, xhr.status === 401 && !!data?.needsStudioKey));
    };
    xhr.send(form);
  });
}

// ───────── types ─────────

export type JobStatus = 'Draft' | 'Queued' | 'Processing' | 'Done' | 'Failed' | 'Cancelled';
export type AudioMode = 'Remove' | 'KeepOriginal' | 'Replace' | 'Mix';

export interface ReupCharacter {
  id: string;
  name: string;
  notes?: string | null;
  consentConfirmed: boolean;
  status: 'Checking' | 'Ready' | 'Rejected' | 'Error';
  rejectReason?: string | null;
  faceScore?: number | null;
  hasThumb: boolean;
  usedByJobs: number;
  createdAtUtc: string;
  checkedAtUtc?: string | null;
}

export interface ReupBackdrop {
  id: string;
  name: string;
  kind: 'Image' | 'Video';
  category: string;
  tags?: string | null;
  mimeType: string;
  sizeBytes: number;
  width: number;
  height: number;
  durationSeconds?: number | null;
  sourceName: string;
  sourceUrl?: string | null;
  author?: string | null;
  licenseName: string;
  licenseUrl?: string | null;
  downloadedAtUtc: string;
  hasThumb: boolean;
  useCount: number;
}

export interface BackdropInfo {
  name?: string;
  category?: string;
  tags?: string;
  sourceName?: string;
  sourceUrl?: string;
  author?: string;
  licenseName?: string;
  licenseUrl?: string;
}

export interface StockItem {
  provider: 'Pexels' | 'Pixabay';
  kind: 'Image' | 'Video';
  id: string;
  previewUrl: string;
  downloadUrl: string;
  pageUrl: string;
  author?: string | null;
  width: number;
  height: number;
  durationSeconds?: number | null;
}

export interface Segment {
  start: number;
  backdropId: string;
}

export interface Placement {
  scale: number;
  offsetX: number;
  offsetY: number;
  harmonize: number;
  lightWrap: number;
  blur: number;
  crossfade: number;
  kenBurns: boolean;
}

export interface JobInput {
  title?: string;
  rightsConfirmed: boolean;
  peopleConsentConfirmed: boolean;
  rightsNote?: string | null;
  faceSwapEnabled: boolean;
  characterId?: string | null;
  backgroundEnabled: boolean;
  segments: Segment[];
  placement: Placement;
  audioMode: AudioMode;
  audioVolume: number;
  originalVolume: number;
  outputShortSide: number;
}

export interface QcCheck {
  name: string;
  ok: boolean;
  detail: string;
}

export interface ReupJob extends JobInput {
  id: string;
  title: string;
  sourceFileName: string;
  sourceSizeBytes: number;
  sourceDurationSeconds: number;
  sourceFps: string;
  sourceFpsValue: number;
  sourceWidth: number;
  sourceHeight: number;
  sourceHasAudio: boolean;
  hasSourceThumb: boolean;
  audioFileName?: string | null;
  outputWidth: number;
  outputHeight: number;
  status: JobStatus;
  previewRun: boolean;
  stage?: string | null;
  progressText?: string | null;
  progressPercent: number;
  framesDone: number;
  framesTotal: number;
  fps?: number | null;
  cancelRequested: boolean;
  workerMachine?: string | null;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  finishedAtUtc?: string | null;
  errorMessage?: string | null;
  hasPreview: boolean;
  hasOutput: boolean;
  outputDurationSeconds?: number | null;
  outputSizeBytes?: number | null;
  qc: QcCheck[];
  stats?: Record<string, number> | null;
  expiresAtUtc?: string | null;
  filesDeleted: boolean;
  characterName?: string | null;
}

export interface ReupSettings {
  threads: number;
  maxRamMb: number;
  workDir?: string | null;
  executionProvider: 'cpu' | 'directml';
  preset: string;
  crf: number;
  outputRetentionDays: number;
  backdropQuotaMb: number;
  pexelsApiKey?: string | null;
  pixabayApiKey?: string | null;
  swapperLicenseConfirmed: boolean;
  swapperLicenseNote?: string | null;
}

export interface EngineStatus {
  ready: boolean;
  error?: string | null;
  checkedAtUtc?: string | null;
  machine?: string | null;
  home?: string | null;
  python?: string | null;
  onnxRuntime?: string | null;
  openCv?: string | null;
  providers: string[];
  models: Record<string, boolean>;
  backgroundFps: Record<string, number>;
  cpuThreads: number;
  ramGb: number;
  freeDiskGb?: number | null;
  ffmpegPath?: string | null;
}

export interface ReupOverview {
  pcOnline: boolean;
  pcMachine?: string | null;
  engine: EngineStatus;
  faceSwapAvailable: boolean;
  faceSwapBlockedReason?: string | null;
  done: number;
  running: number;
  queued: number;
  failed: number;
  characters: number;
  backdrops: number;
  processedMinutes: number;
  storageMb: number;
  backdropStorageMb: number;
  backdropQuotaMb: number;
  stockSearchPexels: boolean;
  stockSearchPixabay: boolean;
  recent: ReupJob[];
}

export const CATEGORIES: { id: string; label: string }[] = [
  { id: 'Beach', label: 'Bãi biển' },
  { id: 'Forest', label: 'Rừng cây' },
  { id: 'Flowers', label: 'Vườn hoa' },
  { id: 'Street', label: 'Đường phố' },
  { id: 'Cafe', label: 'Quán cà phê' },
  { id: 'Apartment', label: 'Căn hộ' },
  { id: 'Pool', label: 'Hồ bơi' },
  { id: 'Nature', label: 'Thiên nhiên' },
  { id: 'Dance', label: 'Sân khấu / nhảy' },
  { id: 'Other', label: 'Khác' },
];

export const categoryLabel = (id: string) => CATEGORIES.find((c) => c.id === id)?.label ?? id;

// ───────── calls ─────────

export const reup = {
  overview: () => call<ReupOverview>('GET', '/overview'),
  settings: () => call<ReupSettings>('GET', '/settings'),
  saveSettings: (s: ReupSettings) => call<ReupSettings>('PUT', '/settings', s),
  checkEngine: (bench: boolean) => call<{ queued: boolean }>('POST', '/engine/check', { bench }),

  characters: () => call<ReupCharacter[]>('GET', '/characters'),
  addCharacter: (file: File, name: string, notes: string, consent: boolean, onProgress?: (p: number) => void) => {
    const f = new FormData();
    f.append('file', file);
    f.append('name', name);
    f.append('notes', notes);
    f.append('consent', String(consent));
    return upload<ReupCharacter>('/characters', f, onProgress);
  },
  updateCharacter: (id: string, body: { name?: string; notes?: string }) => call<ReupCharacter>('PUT', `/characters/${id}`, body),
  recheckCharacter: (id: string) => call<ReupCharacter>('POST', `/characters/${id}/recheck`),
  deleteCharacter: (id: string) => call<void>('DELETE', `/characters/${id}`),
  characterImageUrl: (id: string) => `${BASE}/characters/${id}/image`,
  characterThumbUrl: (id: string) => `${BASE}/characters/${id}/thumb`,

  backdrops: (category?: string, kind?: string, q?: string) => {
    const p = new URLSearchParams();
    if (category) p.set('category', category);
    if (kind) p.set('kind', kind);
    if (q) p.set('q', q);
    return call<ReupBackdrop[]>('GET', `/backdrops?${p.toString()}`);
  },
  addBackdrop: (file: File, info: BackdropInfo, onProgress?: (p: number) => void) => {
    const f = new FormData();
    f.append('file', file);
    Object.entries(info).forEach(([k, v]) => v != null && v !== '' && f.append(k, String(v)));
    return upload<ReupBackdrop>('/backdrops', f, onProgress);
  },
  importBackdrop: (url: string, info: BackdropInfo) => call<ReupBackdrop>('POST', '/backdrops/import', { url, ...info }),
  searchStock: (provider: string, kind: string, q: string) =>
    call<StockItem[]>('GET', `/stock/search?provider=${encodeURIComponent(provider)}&kind=${encodeURIComponent(kind)}&q=${encodeURIComponent(q)}`),
  importStock: (item: StockItem, category: string, name?: string) => call<ReupBackdrop>('POST', '/stock/import', { item, category, name }),
  updateBackdrop: (id: string, info: BackdropInfo) => call<ReupBackdrop>('PUT', `/backdrops/${id}`, info),
  deleteBackdrop: (id: string) => call<void>('DELETE', `/backdrops/${id}`),
  backdropFileUrl: (id: string) => `${BASE}/backdrops/${id}/file`,
  backdropThumbUrl: (id: string) => `${BASE}/backdrops/${id}/thumb`,

  jobs: () => call<ReupJob[]>('GET', '/jobs'),
  job: (id: string) => call<ReupJob>('GET', `/jobs/${id}`),
  createJob: (file: File, title: string, onProgress?: (p: number) => void) => {
    const f = new FormData();
    f.append('file', file);
    f.append('title', title);
    return upload<ReupJob>('/jobs', f, onProgress);
  },
  updateJob: (id: string, input: JobInput) => call<ReupJob>('PUT', `/jobs/${id}`, input),
  setAudio: (id: string, file: File, onProgress?: (p: number) => void) => {
    const f = new FormData();
    f.append('file', file);
    return upload<ReupJob>(`/jobs/${id}/audio`, f, onProgress);
  },
  removeAudio: (id: string) => call<ReupJob>('DELETE', `/jobs/${id}/audio`),
  preview: (id: string) => call<ReupJob>('POST', `/jobs/${id}/preview`),
  start: (id: string) => call<ReupJob>('POST', `/jobs/${id}/start`),
  cancel: (id: string) => call<ReupJob>('POST', `/jobs/${id}/cancel`),
  deleteJob: (id: string) => call<void>('DELETE', `/jobs/${id}`),
  thumbUrl: (id: string) => `${BASE}/jobs/${id}/thumb`,
  sourceUrl: (id: string) => `${BASE}/jobs/${id}/source`,
  previewUrl: (id: string, v?: string) => `${BASE}/jobs/${id}/preview${v ? `?v=${encodeURIComponent(v)}` : ''}`,
  audioUrl: (id: string) => `${BASE}/jobs/${id}/audio`,
  outputUrl: (id: string, download = false, v?: string) =>
    `${BASE}/jobs/${id}/output?${download ? 'download=true&' : ''}${v ? `v=${encodeURIComponent(v)}` : ''}`,
};

export const isActive = (s: JobStatus) => s === 'Queued' || s === 'Processing';
