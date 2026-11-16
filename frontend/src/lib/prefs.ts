export type AiProvider = 'Gemini' | 'Claude';

export interface StudioPrefs {
  aiProvider: AiProvider;
  voice: string;
  speed: string;
  /** Upper bound in seconds — the AI shortens the video when the article has less to say. */
  targetDuration: number;
  hashtags: string;
  /** Facebook Page (account id) new videos are made for: its name goes on the video. '' = ask the server. */
  defaultPageId?: string;
}

export interface VoiceOption {
  id: string;
  name: string;
  desc: string;
  group: 'natural' | 'basic';
}

/**
 * "gemini:*" voices use Gemini TTS (natural, human-like delivery, needs the server's Gemini key).
 * The Edge voices are free but sound more mechanical.
 */
export const VOICES: VoiceOption[] = [
  { id: 'gemini:Kore', name: 'Kore', desc: 'Nữ, rõ ràng, chắc giọng — hợp bản tin.', group: 'natural' },
  { id: 'gemini:Aoede', name: 'Aoede', desc: 'Nữ, nhẹ nhàng, thoải mái — hợp tin đời sống.', group: 'natural' },
  { id: 'gemini:Charon', name: 'Charon', desc: 'Nam, trầm ấm, điềm đạm — hợp thời sự.', group: 'natural' },
  { id: 'gemini:Puck', name: 'Puck', desc: 'Nam, trẻ trung, năng động — hợp tin giải trí.', group: 'natural' },
  { id: 'vi-VN-HoaiMyNeural', name: 'Hoài My', desc: 'Nữ, giọng Bắc. Miễn phí, nghe máy hơn.', group: 'basic' },
  { id: 'vi-VN-NamMinhNeural', name: 'Nam Minh', desc: 'Nam, giọng Bắc. Miễn phí, nghe máy hơn.', group: 'basic' },
];

export const SPEEDS = [
  { id: '+0%', label: 'Chuẩn' },
  { id: '+6%', label: 'Hơi nhanh' },
  { id: '+12%', label: 'Nhanh' },
];

/** Maximum lengths offered (seconds). Default 1:45. */
export const DURATIONS = [60, 105, 150];

export function durationLabel(seconds: number) {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  if (m === 0) return `${s} giây`;
  return s ? `${m} phút ${s}` : `${m} phút`;
}

export const AI_PROVIDERS: { id: AiProvider; name: string; desc: string }[] = [
  { id: 'Gemini', name: 'Gemini', desc: 'Nhanh, chi phí thấp, viết tiếng Việt tự nhiên.' },
  { id: 'Claude', name: 'Claude', desc: 'Văn phong chặt chẽ, phân tích sự kiện kỹ hơn.' },
];

const KEY = 'studio.prefs';
/** Bump when defaults change in a way saved preferences should pick up. */
const PREFS_VERSION = 2;

const DEFAULTS: StudioPrefs = {
  aiProvider: 'Gemini',
  voice: 'gemini:Kore',
  speed: '+0%',
  targetDuration: 105,
  hashtags: '#TinTuc #Reels',
};

export function getPrefs(): StudioPrefs {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return DEFAULTS;
    const stored = JSON.parse(raw);
    if ((stored.v ?? 1) < PREFS_VERSION) {
      // v1 stored the old fixed-60s length and robotic Edge voice as defaults — move to the new ones,
      // keep everything else the user chose.
      const { voice: _v, speed: _s, targetDuration: _t, ...rest } = stored;
      return { ...DEFAULTS, ...rest };
    }
    const merged = { ...DEFAULTS, ...stored };
    if (!DURATIONS.includes(merged.targetDuration)) merged.targetDuration = DEFAULTS.targetDuration;
    return merged;
  } catch {
    return DEFAULTS;
  }
}

export function savePrefs(prefs: Partial<StudioPrefs>) {
  const next = { ...getPrefs(), ...prefs };
  try {
    localStorage.setItem(KEY, JSON.stringify({ ...next, v: PREFS_VERSION }));
  } catch {
    // storage unavailable — keep in-memory defaults
  }
  return next;
}

export function voiceName(id: string) {
  return VOICES.find((v) => v.id === id)?.name ?? id;
}

export function voicePreviewUrl(id: string) {
  return `/api/system/voice-preview?voice=${encodeURIComponent(id)}`;
}
