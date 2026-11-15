import { Film } from 'lucide-react';
import { Badge, Thumb, cn } from './ui';
import { statusMeta, isProcessing } from '../lib/format';
import type { VideoProject } from '../types';

export function StatusBadge({ status }: { status: string }) {
  const meta = statusMeta(status);
  return (
    <Badge tone={meta.tone} dot pulse={isProcessing(status)}>
      {meta.label}
    </Badge>
  );
}

export function projectThumb(p: VideoProject) {
  return p.scenes?.find((s) => s.imageUrl)?.imageUrl ?? null;
}

/** Small 9:16 poster used in lists. */
export function ReelThumb({ src, className }: { src?: string | null; className?: string }) {
  return (
    <Thumb
      src={src}
      className={cn('aspect-[9/16] shrink-0 rounded-[5px] border border-line', className)}
      fallback={<Film className="h-3.5 w-3.5" />}
    />
  );
}
