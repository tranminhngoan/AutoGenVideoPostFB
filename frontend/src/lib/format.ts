export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

interface StatusMeta {
  label: string;
  tone: Tone;
  group: 'queued' | 'processing' | 'review' | 'published' | 'failed';
}

const STATUS: Record<string, StatusMeta> = {
  Pending: { label: 'Chờ xử lý', tone: 'neutral', group: 'queued' },
  Extracting: { label: 'Đang đọc bài', tone: 'info', group: 'processing' },
  Extracted: { label: 'Đã đọc bài', tone: 'neutral', group: 'queued' },
  Analyzing: { label: 'Đang phân tích', tone: 'info', group: 'processing' },
  Scripting: { label: 'Đang viết kịch bản', tone: 'info', group: 'processing' },
  PlanningScenes: { label: 'Đang chia cảnh', tone: 'info', group: 'processing' },
  GeneratingAudio: { label: 'Đang tạo giọng đọc', tone: 'info', group: 'processing' },
  Rendering: { label: 'Đang dựng video', tone: 'info', group: 'processing' },
  QualityChecking: { label: 'Đang kiểm định', tone: 'info', group: 'processing' },
  AwaitingApproval: { label: 'Chờ duyệt', tone: 'warning', group: 'review' },
  Publishing: { label: 'Đang đăng', tone: 'info', group: 'processing' },
  Published: { label: 'Đã đăng', tone: 'success', group: 'published' },
  Failed: { label: 'Lỗi', tone: 'danger', group: 'failed' },
  Cancelled: { label: 'Đã huỷ', tone: 'neutral', group: 'failed' },
};

export function statusMeta(status: string): StatusMeta {
  return STATUS[status] ?? { label: status, tone: 'neutral', group: 'queued' };
}

export function isProcessing(status: string) {
  return statusMeta(status).group === 'processing';
}

const JOB_STATUS: Record<string, { label: string; tone: Tone }> = {
  Pending: { label: 'Đang chờ', tone: 'neutral' },
  Enqueued: { label: 'Đang chờ', tone: 'neutral' },
  Queued: { label: 'Đang chờ', tone: 'neutral' },
  Processing: { label: 'Đang chạy', tone: 'info' },
  Running: { label: 'Đang chạy', tone: 'info' },
  InProgress: { label: 'Đang chạy', tone: 'info' },
  Completed: { label: 'Hoàn tất', tone: 'success' },
  Succeeded: { label: 'Hoàn tất', tone: 'success' },
  Failed: { label: 'Lỗi', tone: 'danger' },
  Cancelled: { label: 'Đã huỷ', tone: 'neutral' },
};

export function jobStatusMeta(status: string) {
  return JOB_STATUS[status] ?? { label: status, tone: 'neutral' as Tone };
}

export function isJobActive(status: string) {
  const t = jobStatusMeta(status);
  return t.tone === 'info' || t.label === 'Đang chờ';
}

export const SCENE_PURPOSE: Record<string, string> = {
  Hook: 'Mở đầu',
  Fact: 'Dữ kiện',
  Context: 'Bối cảnh',
  Analysis: 'Phân tích',
  Conclusion: 'Kết luận',
};

export const MOTION: Record<string, string> = {
  ZoomIn: 'Phóng to',
  ZoomOut: 'Thu nhỏ',
  PanLeft: 'Lia trái',
  PanRight: 'Lia phải',
  KenBurns: 'Ken Burns',
};

export const TRANSITION: Record<string, string> = {
  Fade: 'Mờ dần',
  Slide: 'Trượt',
  Cut: 'Cắt thẳng',
};

export const RIGHTS: Record<string, { label: string; tone: Tone }> = {
  Unknown: { label: 'Chưa rõ quyền', tone: 'neutral' },
  PermissionRequired: { label: 'Cần xin phép', tone: 'warning' },
  Licensed: { label: 'Có giấy phép', tone: 'info' },
  Authorized: { label: 'Được phép dùng', tone: 'success' },
  Rejected: { label: 'Không dùng', tone: 'danger' },
};

export const IMAGE_CATEGORY: Record<string, string> = {
  Panorama: 'Toàn cảnh',
  Character: 'Nhân vật',
  Event: 'Sự kiện',
  Document: 'Tài liệu',
  Chart: 'Biểu đồ',
  Unsuitable: 'Không phù hợp',
};

/** The API returns UTC timestamps that may lack a zone suffix. */
function toDate(value?: string | null): Date | null {
  if (!value) return null;
  const hasZone = /[zZ]|[+-]\d\d:?\d\d$/.test(value);
  const d = new Date(hasZone ? value : value + 'Z');
  return isNaN(d.getTime()) ? null : d;
}

export function formatDateTime(value?: string | null) {
  const d = toDate(value);
  if (!d) return '—';
  return d.toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function formatTime(value?: string | null) {
  const d = toDate(value);
  if (!d) return '—';
  return d.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

export function timeAgo(value?: string | null) {
  const d = toDate(value);
  if (!d) return '—';
  const diff = (Date.now() - d.getTime()) / 1000;
  if (diff < 45) return 'Vừa xong';
  if (diff < 3600) return `${Math.round(diff / 60)} phút trước`;
  if (diff < 86400) return `${Math.round(diff / 3600)} giờ trước`;
  if (diff < 172800) return 'Hôm qua';
  if (diff < 604800) return `${Math.round(diff / 86400)} ngày trước`;
  return d.toLocaleDateString('vi-VN');
}

export function elapsed(start?: string | null, end?: string | null) {
  const a = toDate(start);
  if (!a) return '—';
  const b = toDate(end) ?? new Date();
  const s = Math.max(0, Math.round((b.getTime() - a.getTime()) / 1000));
  if (s < 60) return `${s} giây`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m} phút ${s % 60} giây`;
  return `${Math.floor(m / 60)} giờ ${m % 60} phút`;
}

export function formatClock(seconds?: number | null) {
  if (seconds == null || isNaN(seconds)) return '—';
  const s = Math.round(seconds);
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

export function formatBytes(bytes: number) {
  if (!bytes) return '0 KB';
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function formatNumber(n?: number | null) {
  return (n ?? 0).toLocaleString('vi-VN');
}

export function formatUsd(n?: number | null, digits = 3) {
  return '$' + (n ?? 0).toFixed(digits);
}

export function domainOf(url?: string) {
  if (!url) return '';
  try {
    return new URL(url).hostname.replace(/^www\./, '');
  } catch {
    return url;
  }
}

const PUBLISHERS: [string, string][] = [
  ['vnexpress', 'VnExpress'],
  ['tuoitre', 'Tuổi Trẻ'],
  ['thanhnien', 'Thanh Niên'],
  ['dantri', 'Dân Trí'],
  ['vietnamnet', 'VietNamNet'],
  ['znews', 'Znews'],
  ['zingnews', 'Znews'],
  ['laodong', 'Lao Động'],
  ['tienphong', 'Tiền Phong'],
  ['vtv.vn', 'VTV'],
  ['nld.com', 'Người Lao Động'],
  ['cafef', 'CafeF'],
  ['vov.vn', 'VOV'],
];

export function detectPublisher(url: string) {
  const lower = url.toLowerCase();
  const hit = PUBLISHERS.find(([k]) => lower.includes(k));
  return hit ? hit[1] : domainOf(url) || 'Báo điện tử';
}

export function isValidUrl(value: string) {
  try {
    const u = new URL(value.trim());
    return u.protocol === 'http:' || u.protocol === 'https:';
  } catch {
    return false;
  }
}

export function errorText(err: unknown, fallback: string) {
  if (err instanceof Error && err.message) return err.message;
  return fallback;
}
