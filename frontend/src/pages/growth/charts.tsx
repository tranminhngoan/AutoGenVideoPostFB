import { useEffect, useState, type ReactNode } from 'react';
import { num } from '../../lib/growthMath';

/* Small SVG charts for the growth pages (the project has no chart library). One measure per chart, one axis,
   2px lines / ≤24px bars with 4px rounded data ends, recessive grid, hover crosshair + tooltip, and an explicit
   "Chưa có dữ liệu" state — a missing day is a gap, never a zero. Colours: --viz-1 / --viz-2 (validated pair). */

function useWidth<T extends HTMLElement>() {
  // callback ref: keeps measuring when the element is swapped (empty state → chart)
  const [el, setEl] = useState<T | null>(null);
  const [width, setWidth] = useState(600);
  useEffect(() => {
    if (!el) return;
    setWidth(Math.max(200, Math.floor(el.getBoundingClientRect().width)));
    const ro = new ResizeObserver(([e]) => setWidth(Math.max(200, Math.floor(e.contentRect.width))));
    ro.observe(el);
    return () => ro.disconnect();
  }, [el]);
  return [setEl, width] as const;
}

function niceMax(v: number) {
  if (v <= 0) return 1;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  const n = v / p;
  return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * p;
}

export function ChartEmpty({ children }: { children: ReactNode }) {
  return (
    <div className="flex h-40 items-center justify-center rounded-md border border-dashed border-line px-4 text-center text-[13px] text-fg-subtle">
      {children}
    </div>
  );
}

export interface LinePoint {
  label: string;
  value: number | null | undefined;
  /** marks the days before the campaign (drawn on a muted band) */
  muted?: boolean;
}

/** Day series: one line, gaps where Meta gave nothing. */
export function LineChart({
  points,
  unit = '',
  height = 180,
  empty,
  ariaLabel,
}: {
  points: LinePoint[];
  unit?: string;
  height?: number;
  empty: ReactNode;
  ariaLabel: string;
}) {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [hover, setHover] = useState<number | null>(null);
  const values = points.map((p) => (p.value == null ? null : p.value));
  const present = values.filter((v): v is number => v != null);
  if (present.length === 0)
    return (
      <div ref={ref}>
        <ChartEmpty>{empty}</ChartEmpty>
      </div>
    );

  const padL = 44, padR = 12, padT = 12, padB = 24;
  const w = width - padL - padR;
  const h = height - padT - padB;
  const min = Math.min(0, ...present);
  const max = niceMax(Math.max(...present));
  const x = (i: number) => padL + (points.length === 1 ? w / 2 : (i * w) / (points.length - 1));
  const y = (v: number) => padT + h - ((v - min) / (max - min || 1)) * h;
  const segments: string[] = [];
  let cur = '';
  values.forEach((v, i) => {
    if (v == null) {
      if (cur) segments.push(cur);
      cur = '';
    } else cur += `${cur ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)}`;
  });
  if (cur) segments.push(cur);
  const mutedEnd = points.findIndex((p) => !p.muted);
  const step = Math.max(1, Math.ceil(points.length / Math.max(2, Math.floor(w / 56))));

  const onMove = (e: React.PointerEvent<SVGSVGElement>) => {
    const r = (e.currentTarget as SVGSVGElement).getBoundingClientRect();
    const px = e.clientX - r.left;
    const i = Math.round(((px - padL) / (w || 1)) * (points.length - 1));
    setHover(Math.max(0, Math.min(points.length - 1, i)));
  };

  return (
    <div ref={ref} className="relative w-full min-w-0 overflow-hidden">
      <svg width={width} height={height} role="img" aria-label={ariaLabel} onPointerMove={onMove} onPointerLeave={() => setHover(null)} className="block touch-none">
        {mutedEnd > 0 && (
          <rect x={padL} y={padT} width={Math.max(0, x(mutedEnd) - padL - (points.length > 1 ? w / (points.length - 1) / 2 : 0))} height={h} className="fill-muted" opacity={0.6} />
        )}
        {[0, 0.5, 1].map((t) => {
          const v = min + (max - min) * t;
          return (
            <g key={t}>
              <line x1={padL} x2={padL + w} y1={y(v)} y2={y(v)} className="stroke-line" strokeWidth={1} />
              <text x={padL - 6} y={y(v) + 4} textAnchor="end" className="fill-fg-subtle text-[11px] tabular-nums">
                {num(v)}
              </text>
            </g>
          );
        })}
        {points.map((p, i) =>
          (i % step === 0 && points.length - 1 - i >= step / 2) || i === points.length - 1 ? (
            <text key={i} x={x(i)} y={height - 6} textAnchor="middle" className="fill-fg-subtle text-[11px]">
              {p.label}
            </text>
          ) : null
        )}
        {segments.map((d, i) => (
          <path key={i} d={d} fill="none" stroke="var(--viz-1)" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
        ))}
        {/* lone points (a value between two gaps) stay visible */}
        {values.map((v, i) =>
          v != null && values[i - 1] == null && values[i + 1] == null ? (
            <circle key={i} cx={x(i)} cy={y(v)} r={4} fill="var(--viz-1)" className="stroke-surface" strokeWidth={2} />
          ) : null
        )}
        {hover != null && (
          <g pointerEvents="none">
            <line x1={x(hover)} x2={x(hover)} y1={padT} y2={padT + h} className="stroke-line-strong" strokeWidth={1} />
            {values[hover] != null && <circle cx={x(hover)} cy={y(values[hover]!)} r={4.5} fill="var(--viz-1)" className="stroke-surface" strokeWidth={2} />}
          </g>
        )}
      </svg>
      {hover != null && (
        <div
          className="pointer-events-none absolute top-1 z-10 rounded-md border border-line bg-surface px-2.5 py-1.5 text-xs shadow-pop"
          style={{ left: Math.min(Math.max(0, x(hover) - 60), width - 130) }}
        >
          <div className="text-fg-subtle">{points[hover].label}{points[hover].muted ? ' · trước chiến dịch' : ''}</div>
          <div className="font-semibold tabular-nums text-fg">{values[hover] == null ? 'Chưa có dữ liệu' : num(values[hover], unit)}</div>
        </div>
      )}
    </div>
  );
}

/** One value per category (e.g. per content kind), horizontal bars with the value written at the end. */
export function BarList({ items, unit = '', empty }: { items: { label: string; value: number | null | undefined; hint?: string }[]; unit?: string; empty: ReactNode }) {
  const present = items.filter((i) => i.value != null);
  if (present.length === 0) return <ChartEmpty>{empty}</ChartEmpty>;
  const max = Math.max(...present.map((i) => i.value as number), 1);
  return (
    <div className="space-y-2.5" role="list">
      {items.map((i) => (
        <div key={i.label} role="listitem" className="group grid grid-cols-[88px_1fr_auto] items-center gap-3 text-[13px]" title={i.hint}>
          <span className="truncate text-fg-muted">{i.label}</span>
          <div className="h-3.5 rounded-r bg-muted/40">
            {i.value != null && (
              <div className="h-full rounded-r-[4px] transition-opacity group-hover:opacity-80" style={{ width: `${Math.max(2, ((i.value as number) / max) * 100)}%`, background: 'var(--viz-1)' }} />
            )}
          </div>
          <span className="w-16 text-right font-medium tabular-nums text-fg">{i.value == null ? '—' : num(i.value, unit)}</span>
        </div>
      ))}
    </div>
  );
}

/** Before vs during the campaign, one pair of bars per measure; legend + values written on (identity never colour-alone). */
export function PairBars({ rows, empty }: { rows: { label: string; before: number | null | undefined; during: number | null | undefined }[]; empty: ReactNode }) {
  const usable = rows.filter((r) => r.before != null || r.during != null);
  if (usable.length === 0) return <ChartEmpty>{empty}</ChartEmpty>;
  return (
    <div>
      <div className="mb-3 flex items-center gap-4 text-xs text-fg-muted">
        <span className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-sm" style={{ background: 'var(--viz-1)' }} /> 7 ngày trước</span>
        <span className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-sm" style={{ background: 'var(--viz-2)' }} /> Trong chiến dịch</span>
      </div>
      <div className="space-y-4">
        {usable.map((r) => {
          const max = Math.max(r.before ?? 0, r.during ?? 0, 1);
          return (
            <div key={r.label}>
              <div className="mb-1.5 text-[13px] text-fg-muted">{r.label}</div>
              {([['before', 'var(--viz-1)', '7 ngày trước'], ['during', 'var(--viz-2)', 'Trong chiến dịch']] as const).map(([k, color, name]) => {
                const v = r[k];
                return (
                  <div key={k} className="mb-[2px] grid grid-cols-[1fr_auto] items-center gap-3" title={`${name}: ${v == null ? 'chưa có dữ liệu' : num(v)}`}>
                    <div className="h-3">
                      {v != null && <div className="h-full rounded-r-[4px]" style={{ width: `${Math.max(1.5, (v / max) * 100)}%`, background: color }} />}
                    </div>
                    <span className="w-16 text-right text-xs tabular-nums text-fg">{v == null ? '—' : num(v)}</span>
                  </div>
                );
              })}
            </div>
          );
        })}
      </div>
    </div>
  );
}

/** Hour-of-day columns (average views), single hue. */
export function HourColumns({ buckets, highlight }: { buckets: { hour: number; label: string; posts: number; avgViews?: number | null }[]; highlight: string[] }) {
  const [ref, width] = useWidth<HTMLDivElement>();
  const [hover, setHover] = useState<number | null>(null);
  const height = 160, padB = 22, padT = 8;
  const max = niceMax(Math.max(1, ...buckets.map((b) => b.avgViews ?? 0)));
  const slot = width / 24;
  const bar = Math.min(24, Math.max(4, slot - 4));
  return (
    <div ref={ref} className="relative w-full min-w-0 overflow-hidden">
      <svg width={width} height={height} role="img" aria-label="Lượt xem trung bình theo giờ đăng" className="block">
        <line x1={0} x2={width} y1={height - padB} y2={height - padB} className="stroke-line" />
        {Array.from({ length: 24 }, (_, hr) => {
          const b = buckets.find((x) => x.hour === hr);
          const v = b?.avgViews ?? 0;
          const bh = b ? Math.max(2, (v / max) * (height - padB - padT)) : 0;
          const cx = hr * slot + slot / 2;
          const strong = highlight.includes(`${String(hr).padStart(2, '0')}:00`);
          return (
            <g key={hr} onPointerEnter={() => setHover(hr)} onPointerLeave={() => setHover(null)}>
              <rect x={hr * slot} y={0} width={slot} height={height} fill="transparent" />
              {b && (
                <path
                  d={`M${cx - bar / 2},${height - padB} V${height - padB - bh + 4} Q${cx - bar / 2},${height - padB - bh} ${cx - bar / 2 + 4},${height - padB - bh} H${cx + bar / 2 - 4} Q${cx + bar / 2},${height - padB - bh} ${cx + bar / 2},${height - padB - bh + 4} V${height - padB} Z`}
                  fill="var(--viz-1)"
                  opacity={strong || hover === hr ? 1 : 0.55}
                />
              )}
              {hr % 3 === 0 && (
                <text x={cx} y={height - 6} textAnchor="middle" className="fill-fg-subtle text-[11px]">
                  {hr}h
                </text>
              )}
            </g>
          );
        })}
      </svg>
      {hover != null && (
        <div className="pointer-events-none absolute top-0 z-10 rounded-md border border-line bg-surface px-2.5 py-1.5 text-xs shadow-pop" style={{ left: Math.min(hover * slot, width - 150) }}>
          {(() => {
            const b = buckets.find((x) => x.hour === hover);
            return b ? (
              <>
                <div className="text-fg-subtle">{b.label} · {b.posts} bài</div>
                <div className="font-semibold tabular-nums text-fg">{num(b.avgViews)} lượt xem TB</div>
              </>
            ) : (
              <div className="text-fg-subtle">{String(hover).padStart(2, '0')}:00 · chưa có bài</div>
            );
          })()}
        </div>
      )}
    </div>
  );
}
