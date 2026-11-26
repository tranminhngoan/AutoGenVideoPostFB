/* Pure helpers of the growth-campaign pages (no React): Vietnam time, KPI formulas, labels, calendar grids.
   Kept dependency-free so they can be tested with plain Node (see growthMath.test.ts). */

export const VN_OFFSET_MIN = 7 * 60;

/** Vietnam wall-clock parts of a UTC instant (independent of the browser's own time zone). */
export function vnParts(utc: string | Date) {
  const d = new Date(typeof utc === 'string' ? utc : utc.getTime());
  const shifted = new Date(d.getTime() + VN_OFFSET_MIN * 60000);
  return {
    y: shifted.getUTCFullYear(),
    m: shifted.getUTCMonth() + 1,
    d: shifted.getUTCDate(),
    hh: shifted.getUTCHours(),
    mm: shifted.getUTCMinutes(),
    dow: shifted.getUTCDay(),
  };
}

const pad = (n: number) => String(n).padStart(2, '0');

/** "2026-11-26" — the Vietnam date of a UTC instant. */
export function vnDate(utc: string | Date): string {
  const p = vnParts(utc);
  return `${p.y}-${pad(p.m)}-${pad(p.d)}`;
}

/** "07:30" — Vietnam time of a UTC instant. */
export function vnTime(utc: string | Date): string {
  const p = vnParts(utc);
  return `${pad(p.hh)}:${pad(p.mm)}`;
}

/** "26/11 07:30" */
export function vnShort(utc?: string | null): string {
  if (!utc) return '—';
  const p = vnParts(utc);
  return `${pad(p.d)}/${pad(p.m)} ${pad(p.hh)}:${pad(p.mm)}`;
}

/** UTC ISO string of a Vietnam date ("2026-11-26") + time ("07:30"). */
export function vnToUtc(date: string, time: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const [hh, mm] = time.split(':').map(Number);
  return new Date(Date.UTC(y, m - 1, d, hh, mm) - VN_OFFSET_MIN * 60000).toISOString();
}

/** Today's Vietnam date. */
export function vnToday(now: Date = new Date()): string {
  return vnDate(now);
}

/** Adds days to a "yyyy-mm-dd" date. */
export function addDays(date: string, days: number): string {
  const [y, m, d] = date.split('-').map(Number);
  const t = new Date(Date.UTC(y, m - 1, d + days));
  return `${t.getUTCFullYear()}-${pad(t.getUTCMonth() + 1)}-${pad(t.getUTCDate())}`;
}

export function dayOfWeek(date: string): number {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay();
}

/** Monday of the week containing <date>. */
export function weekStart(date: string): string {
  const dow = dayOfWeek(date);
  return addDays(date, dow === 0 ? -6 : 1 - dow);
}

/** 7 dates from Monday. */
export function weekDays(date: string): string[] {
  const start = weekStart(date);
  return Array.from({ length: 7 }, (_, i) => addDays(start, i));
}

/** Month grid: full weeks (Mon–Sun) covering the month of <date>. */
export function monthGrid(date: string): string[] {
  const first = date.slice(0, 8) + '01';
  const start = weekStart(first);
  const [y, m] = date.split('-').map(Number);
  const nextMonth = m === 12 ? `${y + 1}-01-01` : `${y}-${pad(m + 1)}-01`;
  const cells: string[] = [];
  for (let d = start; d < nextMonth || cells.length % 7 !== 0; d = addDays(d, 1)) cells.push(d);
  return cells;
}

export const WEEKDAY_SHORT = ['CN', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7'];

/** "26/11" */
export function dm(date: string): string {
  const [, m, d] = date.split('-');
  return `${d}/${m}`;
}

// ───────── KPI formulas (same as the server; null when data is missing — never 0) ─────────

export function growthPercent(newFollowers?: number | null, starting?: number | null): number | null {
  if (newFollowers == null || starting == null || starting <= 0) return null;
  return Math.round((newFollowers * 10000) / starting) / 100;
}

export function conversionPercent(newFollowers?: number | null, visits?: number | null): number | null {
  if (newFollowers == null || visits == null || visits <= 0) return null;
  return Math.round((newFollowers * 10000) / visits) / 100;
}

/** Compact Vietnamese number: 1.234, 12,3K, 1,2Tr; "—" when missing. */
export function num(v?: number | null, unit = ''): string {
  if (v == null || Number.isNaN(v)) return '—';
  const abs = Math.abs(v);
  let s: string;
  if (unit === '%') s = v.toLocaleString('vi-VN', { maximumFractionDigits: 2 });
  else if (abs >= 1_000_000) s = (v / 1_000_000).toLocaleString('vi-VN', { maximumFractionDigits: 1 }) + 'Tr';
  else if (abs >= 10_000) s = (v / 1000).toLocaleString('vi-VN', { maximumFractionDigits: 1 }) + 'K';
  else s = v.toLocaleString('vi-VN', { maximumFractionDigits: 1 });
  return unit ? `${s}${unit}` : s;
}

// ───────── labels ─────────

export const KIND_LABEL: Record<string, string> = {
  Reel: 'Reels',
  Photo: 'Ảnh đơn',
  Album: 'Nhiều ảnh',
  Story: 'Story',
  Interactive: 'Tương tác',
};

export const STATUS_LABEL: Record<string, string> = {
  Draft: 'Nháp',
  PendingApproval: 'Chờ duyệt',
  Scheduled: 'Đã lên lịch',
  Publishing: 'Đang đăng',
  Published: 'Đã đăng',
  Failed: 'Thất bại',
  Cancelled: 'Đã huỷ',
};

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export const STATUS_TONE: Record<string, Tone> = {
  Draft: 'neutral',
  PendingApproval: 'warning',
  Scheduled: 'info',
  Publishing: 'info',
  Published: 'success',
  Failed: 'danger',
  Cancelled: 'neutral',
};

export const CAMPAIGN_STATUS: Record<string, { label: string; tone: Tone }> = {
  Draft: { label: 'Chưa bắt đầu', tone: 'neutral' },
  Running: { label: 'Đang chạy', tone: 'success' },
  Paused: { label: 'Tạm dừng', tone: 'warning' },
  Completed: { label: 'Hoàn thành', tone: 'info' },
  Cancelled: { label: 'Đã huỷ', tone: 'neutral' },
};

export const STYLE_LABEL: Record<string, string> = {
  Emotional: 'Cảm xúc',
  Nostalgic: 'Gợi ký ức',
  Story: 'Kể chuyện',
  Curiosity: 'Gợi tò mò',
  Discussion: 'Thảo luận',
  Knowledge: 'Chia sẻ kiến thức',
  Short: 'Ngắn gọn',
};

/** Count of distinct hashtags in a caption + hashtag field (the server refuses more than 10). */
export function countHashtags(caption: string, hashtags: string): number {
  const m = `${caption} ${hashtags}`.match(/#[\p{L}\p{N}_]+/gu) || [];
  return new Set(m.map((x) => x.toLowerCase())).size;
}

/** Fresh idempotency key for one "create" click. */
export function newKey(): string {
  const c = (globalThis as any).crypto;
  return c?.randomUUID ? c.randomUUID().replace(/-/g, '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 12)}`;
}

/** Splits text into lines that fit <maxChars> (word wrap for the image templates). */
export function wrapWords(text: string, maxChars: number): string[] {
  const words = text.split(/\s+/).filter(Boolean);
  const lines: string[] = [];
  let line = '';
  for (const w of words) {
    if (!line) line = w;
    else if ((line + ' ' + w).length <= maxChars) line += ' ' + w;
    else {
      lines.push(line);
      line = w;
    }
  }
  if (line) lines.push(line);
  return lines;
}
