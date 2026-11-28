import { useCallback, useEffect, useState } from 'react';
import { Copy, Check } from 'lucide-react';
import { Button, Card, cn } from '../../components/ui';
import { useToast } from '../../components/feedback';
import { copyText } from './outreachApi';

/** Loads data once (and on demand), reporting errors as a toast. */
export function useLoad<T>(fn: () => Promise<T>, deps: unknown[] = []) {
  const toast = useToast();
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const reload = useCallback(async () => {
    setLoading(true);
    try {
      setData(await fn());
    } catch (e) {
      toast.error('Không tải được dữ liệu', (e as Error).message);
    } finally {
      setLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
  useEffect(() => {
    reload();
  }, [reload]);
  return { data, setData, loading, reload };
}

export function Stat({ label, value, hint, accent }: { label: string; value: React.ReactNode; hint?: React.ReactNode; accent?: boolean }) {
  return (
    <Card className={cn('p-3.5', accent && 'border-rose-500/30 bg-gradient-to-br from-rose-500/[0.06] to-fuchsia-500/[0.06]')}>
      <p className="text-xs text-fg-muted">{label}</p>
      <p className="mt-1 text-2xl font-semibold tabular-nums text-fg">{value}</p>
      {hint && <p className="mt-0.5 text-[11px] text-fg-subtle">{hint}</p>}
    </Card>
  );
}

export function CopyButton({ text, label = 'Copy', onCopied, size = 'sm', variant = 'secondary' }: {
  text: string;
  label?: string;
  onCopied?: () => void;
  size?: 'sm' | 'md';
  variant?: 'secondary' | 'primary' | 'ghost' | 'accent';
}) {
  const toast = useToast();
  const [done, setDone] = useState(false);
  return (
    <Button
      size={size}
      variant={variant}
      icon={done ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
      onClick={async () => {
        if (await copyText(text)) {
          setDone(true);
          onCopied?.();
          setTimeout(() => setDone(false), 1500);
        } else toast.error('Trình duyệt không cho copy — bôi đen và copy tay nhé');
      }}
    >
      {done ? 'Đã copy' : label}
    </Button>
  );
}

export function PageAvatar({ url, name, size = 36 }: { url?: string; name: string; size?: number }) {
  return url ? (
    <img src={url} alt={name} width={size} height={size} className="shrink-0 rounded-full border border-line bg-muted object-cover" style={{ width: size, height: size }} />
  ) : (
    <span className="flex shrink-0 items-center justify-center rounded-full bg-muted text-xs font-semibold text-fg-muted" style={{ width: size, height: size }}>
      {name.slice(0, 1)}
    </span>
  );
}

export const fmtDate = (v?: string | null) => (v ? new Date(v).toLocaleDateString('vi-VN') : '—');
export const fmtDateTime = (v?: string | null) =>
  v ? new Date(v).toLocaleString('vi-VN', { hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit' }) : '—';
export const rate = (clicks: number, views: number) => (views > 0 ? `${Math.round((100 * clicks) / views)}%` : '—');
