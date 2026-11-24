/* Reup Video API client — free processing on the owner's PC. Writes need the same X-Studio-Key as the AI video studio
   (kept only in this browser). Files go straight to the Railway API in 4 MB pieces (a failed piece is sent again). */
import { getStudioKey, StudioError } from './studio';

const BASE = '/api/reup';
const CHUNK = 4 * 1024 * 1024;
/** Uploads skip the Vercel proxy (it drops request bodies of a few MB with a connection error) and go straight to
    the Railway API — the same address vercel.json forwards /api to. Locally they stay on the Vite proxy. */
const RAILWAY_API = 'https://postnewsauto.up.railway.app/api/reup';
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

/** Sends a file piece by piece (progress 0–1); returns the upload id the form then refers to. */
async function sendFile(file: File, onProgress?: (p: number) => void): Promise<string> {
  const { uploadId } = await read<{ uploadId: string }>(await fetch(`${UPLOAD_BASE}/uploads`, { method: 'POST', headers: headers() }));
  onProgress?.(0);
  for (let offset = 0; offset < file.size; offset += CHUNK) {
    const piece = file.slice(offset, offset + CHUNK);
    for (let attempt = 1; ; attempt++) {
      try {
        const res = await fetch(`${UPLOAD_BASE}/uploads/${uploadId}?offset=${offset}`, {
          method: 'PUT',
          headers: headers({ 'Content-Type': 'application/octet-stream' }),
          body: piece,
        });
        await read(res);
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

/** Sends the file in pieces, then the form (with the upload id instead of the file). */
async function upload<T>(path: string, file: File, fields: Record<string, string | undefined | null>, onProgress?: (p: number) => void): Promise<T> {
  const uploadId = await sendFile(file, onProgress);
  const form = new FormData();
  form.append('uploadId', uploadId);
  form.append('fileName', file.name);
  form.append('contentType', file.type || '');
  Object.entries(fields).forEach(([k, v]) => v != null && v !== '' && form.append(k, String(v)));
  // the pieces sit on the server that received them: the form goes to the same address
  return read<T>(await fetch(`${UPLOAD_BASE}${path}`, { method: 'POST', headers: headers(), body: form }));
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
  quality: 'high' | 'fast';
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
  pcUpdating: boolean;
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
  addCharacter: (file: File, name: string, notes: string, consent: boolean, onProgress?: (p: number) => void) =>
    upload<ReupCharacter>('/characters', file, { name, notes, consent: String(consent) }, onProgress),
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
  addBackdrop: (file: File, info: BackdropInfo, onProgress?: (p: number) => void) =>
    upload<ReupBackdrop>('/backdrops', file, { ...info }, onProgress),
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
  createJob: (file: File, title: string, onProgress?: (p: number) => void) => upload<ReupJob>('/jobs', file, { title }, onProgress),
  updateJob: (id: string, input: JobInput) => call<ReupJob>('PUT', `/jobs/${id}`, input),
  setAudio: (id: string, file: File, onProgress?: (p: number) => void) => upload<ReupJob>(`/jobs/${id}/audio`, file, {}, onProgress),
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
