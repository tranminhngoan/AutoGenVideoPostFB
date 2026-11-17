import React, { useEffect, useRef, useState } from 'react';
import { ChevronDown, Loader2, X, ImageOff } from 'lucide-react';
import type { Tone } from '../lib/format';

export function cn(...parts: (string | number | false | null | undefined)[]) {
  return parts.filter(Boolean).join(' ');
}

/* ───────────────────────── Button ───────────────────────── */

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'danger-soft' | 'accent';
type ButtonSize = 'sm' | 'md' | 'lg';

const buttonBase =
  'inline-flex items-center justify-center gap-2 whitespace-nowrap font-medium rounded-md transition-colors select-none disabled:opacity-50 disabled:pointer-events-none';

const buttonVariants: Record<ButtonVariant, string> = {
  primary: 'bg-fg text-canvas hover:bg-fg/90',
  accent: 'bg-accent text-accent-fg hover:bg-accent/90',
  secondary: 'bg-surface text-fg border border-line-strong hover:bg-muted shadow-card',
  ghost: 'text-fg-muted hover:text-fg hover:bg-muted',
  danger: 'bg-danger text-white hover:bg-danger/90',
  'danger-soft': 'text-danger hover:bg-danger/10',
};

const buttonSizes: Record<ButtonSize, string> = {
  sm: 'h-8 px-3 text-[13px]',
  md: 'h-9 px-3.5 text-sm',
  lg: 'h-10 px-4 text-sm',
};

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  icon?: React.ReactNode;
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ variant = 'secondary', size = 'md', loading, icon, className, children, disabled, type = 'button', ...rest }, ref) => (
    <button
      ref={ref}
      type={type}
      disabled={disabled || loading}
      className={cn(buttonBase, buttonVariants[variant], buttonSizes[size], className)}
      {...rest}
    >
      {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : icon}
      {children}
    </button>
  )
);
Button.displayName = 'Button';

export function ButtonLink({
  variant = 'secondary',
  size = 'md',
  icon,
  className,
  children,
  ...rest
}: React.AnchorHTMLAttributes<HTMLAnchorElement> & { variant?: ButtonVariant; size?: ButtonSize; icon?: React.ReactNode }) {
  return (
    <a className={cn(buttonBase, buttonVariants[variant], buttonSizes[size], className)} {...rest}>
      {icon}
      {children}
    </a>
  );
}

export function IconButton({
  label,
  className,
  children,
  size = 'md',
  ...rest
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { label: string; size?: 'sm' | 'md' }) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      className={cn(
        'inline-flex items-center justify-center rounded-md text-fg-muted hover:text-fg hover:bg-muted transition-colors disabled:opacity-50 disabled:pointer-events-none',
        size === 'sm' ? 'h-7 w-7' : 'h-9 w-9',
        className
      )}
      {...rest}
    >
      {children}
    </button>
  );
}

/* ───────────────────────── Layout bits ───────────────────────── */

export function PageHeader({
  title,
  description,
  actions,
  back,
}: {
  title: React.ReactNode;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  back?: React.ReactNode;
}) {
  return (
    <div className="mb-5 sm:mb-6">
      {back && <div className="mb-3">{back}</div>}
      {/* Title and actions share one row on every screen size; the description wraps underneath */}
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="text-xl font-semibold leading-tight text-fg sm:text-[22px]">{title}</h1>
          {description && <div className="mt-1 text-[13px] text-fg-muted sm:text-sm">{description}</div>}
        </div>
        {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
      </div>
    </div>
  );
}

export function Card({ className, children, ...rest }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('rounded-lg border border-line bg-surface shadow-card', className)} {...rest}>
      {children}
    </div>
  );
}

export function CardHeader({
  title,
  description,
  actions,
  className,
}: {
  title: React.ReactNode;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={cn('flex flex-wrap items-start justify-between gap-x-4 gap-y-3 px-5 pt-4', className)}>
      <div className="min-w-0 flex-1 basis-56">
        <h2 className="text-[15px] font-semibold text-fg">{title}</h2>
        {description && <p className="mt-0.5 text-[13px] text-fg-muted">{description}</p>}
      </div>
      {actions && <div className="flex max-w-full flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}

export function CardBody({ className, children }: { className?: string; children: React.ReactNode }) {
  return <div className={cn('px-5 py-4', className)}>{children}</div>;
}

export function Divider({ className }: { className?: string }) {
  return <div className={cn('h-px bg-line', className)} />;
}

/* ───────────────────────── Badge ───────────────────────── */

const toneClasses: Record<Tone, string> = {
  neutral: 'bg-muted text-fg-muted',
  info: 'bg-accent/10 text-accent',
  success: 'bg-success/10 text-success',
  warning: 'bg-warning/10 text-warning',
  danger: 'bg-danger/10 text-danger',
};

const dotClasses: Record<Tone, string> = {
  neutral: 'bg-fg-subtle',
  info: 'bg-accent',
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-danger',
};

export function Badge({
  tone = 'neutral',
  dot,
  pulse,
  className,
  children,
}: {
  tone?: Tone;
  dot?: boolean;
  pulse?: boolean;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <span
      className={cn(
        'inline-flex h-[22px] items-center gap-1.5 whitespace-nowrap rounded-full px-2 text-xs font-medium',
        toneClasses[tone],
        className
      )}
    >
      {dot && <Dot tone={tone} pulse={pulse} />}
      {children}
    </span>
  );
}

export function Dot({ tone = 'neutral', pulse }: { tone?: Tone; pulse?: boolean }) {
  return (
    <span className="relative inline-flex h-1.5 w-1.5">
      {pulse && <span className={cn('absolute inset-0 animate-ping rounded-full opacity-60', dotClasses[tone])} />}
      <span className={cn('relative inline-flex h-1.5 w-1.5 rounded-full', dotClasses[tone])} />
    </span>
  );
}

/* ───────────────────────── Alert ───────────────────────── */

export function Alert({
  tone = 'info',
  icon,
  title,
  children,
  action,
  className,
}: {
  tone?: Tone;
  icon?: React.ReactNode;
  title?: React.ReactNode;
  children?: React.ReactNode;
  action?: React.ReactNode;
  className?: string;
}) {
  const border: Record<Tone, string> = {
    neutral: 'border-line bg-subtle',
    info: 'border-accent/25 bg-accent/[0.06]',
    success: 'border-success/25 bg-success/[0.06]',
    warning: 'border-warning/30 bg-warning/[0.07]',
    danger: 'border-danger/25 bg-danger/[0.06]',
  };
  const text: Record<Tone, string> = {
    neutral: 'text-fg-muted',
    info: 'text-accent',
    success: 'text-success',
    warning: 'text-warning',
    danger: 'text-danger',
  };
  return (
    <div className={cn('flex flex-wrap gap-x-3 gap-y-2 rounded-lg border px-4 py-3 text-[13px] sm:flex-nowrap', border[tone], className)}>
      {icon && <span className={cn('mt-0.5 shrink-0 [&>svg]:h-4 [&>svg]:w-4', text[tone])}>{icon}</span>}
      <div className="min-w-0 flex-1">
        {title && <div className="font-medium text-fg">{title}</div>}
        {children && <div className={cn('text-fg-muted', title && 'mt-0.5')}>{children}</div>}
      </div>
      {action && <div className="w-full shrink-0 pl-7 sm:w-auto sm:self-center sm:pl-0">{action}</div>}
    </div>
  );
}

/* ───────────────────────── Form controls ───────────────────────── */

const fieldBase =
  'w-full rounded-md border border-line-strong bg-surface text-fg placeholder:text-fg-subtle transition-shadow focus:border-accent focus:ring-[3px] focus:ring-accent/15 disabled:opacity-60';

export const Input = React.forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement> & { leading?: React.ReactNode }>(
  ({ className, leading, ...rest }, ref) =>
    leading ? (
      <div className="relative">
        <span className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-fg-subtle [&>svg]:h-4 [&>svg]:w-4">
          {leading}
        </span>
        <input ref={ref} className={cn(fieldBase, 'h-9 pl-9 pr-3 text-sm', className)} {...rest} />
      </div>
    ) : (
      <input ref={ref} className={cn(fieldBase, 'h-9 px-3 text-sm', className)} {...rest} />
    )
);
Input.displayName = 'Input';

export const Textarea = React.forwardRef<HTMLTextAreaElement, React.TextareaHTMLAttributes<HTMLTextAreaElement>>(
  ({ className, ...rest }, ref) => (
    <textarea ref={ref} className={cn(fieldBase, 'px-3 py-2 text-sm leading-relaxed', className)} {...rest} />
  )
);
Textarea.displayName = 'Textarea';

export function Select({
  className,
  children,
  size = 'md',
  ...rest
}: Omit<React.SelectHTMLAttributes<HTMLSelectElement>, 'size'> & { size?: 'sm' | 'md' }) {
  return (
    <div className={cn('relative', className)}>
      <select
        className={cn(
          fieldBase,
          'appearance-none pr-8 cursor-pointer',
          size === 'sm' ? 'h-8 pl-2.5 text-[13px]' : 'h-9 pl-3 text-sm'
        )}
        {...rest}
      >
        {children}
      </select>
      <ChevronDown className="pointer-events-none absolute right-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-fg-subtle" />
    </div>
  );
}

export function Field({
  label,
  hint,
  error,
  htmlFor,
  optional,
  children,
  className,
}: {
  label: React.ReactNode;
  hint?: React.ReactNode;
  error?: React.ReactNode;
  htmlFor?: string;
  optional?: boolean;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={className}>
      <label htmlFor={htmlFor} className="mb-1.5 flex items-baseline gap-1.5 text-[13px] font-medium text-fg">
        {label}
        {optional && <span className="font-normal text-fg-subtle">(không bắt buộc)</span>}
      </label>
      {children}
      {error ? (
        <p className="mt-1.5 text-xs text-danger">{error}</p>
      ) : hint ? (
        <p className="mt-1.5 text-xs text-fg-subtle">{hint}</p>
      ) : null}
    </div>
  );
}

export function Checkbox({
  checked,
  onChange,
  label,
  description,
  disabled,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  label: React.ReactNode;
  description?: React.ReactNode;
  disabled?: boolean;
}) {
  return (
    <label className={cn('flex cursor-pointer items-start gap-2.5', disabled && 'opacity-60 pointer-events-none')}>
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        className="mt-0.5 h-4 w-4 shrink-0 cursor-pointer rounded border-line-strong accent-[rgb(var(--accent))]"
      />
      <span className="min-w-0">
        <span className="block text-sm text-fg">{label}</span>
        {description && <span className="mt-0.5 block text-xs text-fg-muted">{description}</span>}
      </span>
    </label>
  );
}

export function Segmented<T extends string | number>({
  options,
  value,
  onChange,
  size = 'md',
  className,
}: {
  options: { value: T; label: React.ReactNode }[];
  value: T;
  onChange: (v: T) => void;
  size?: 'sm' | 'md';
  className?: string;
}) {
  return (
    <div className={cn('inline-flex rounded-md border border-line bg-muted p-0.5', className)} role="radiogroup">
      {options.map((o) => (
        <button
          key={String(o.value)}
          type="button"
          role="radio"
          aria-checked={value === o.value}
          onClick={() => onChange(o.value)}
          className={cn(
            'whitespace-nowrap rounded-[5px] font-medium transition-colors',
            size === 'sm' ? 'h-7 px-2.5 text-[13px]' : 'h-8 px-3 text-sm',
            value === o.value ? 'bg-surface text-fg shadow-card' : 'text-fg-muted hover:text-fg'
          )}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

export function RadioCard({
  checked,
  onClick,
  title,
  description,
  aside,
}: {
  checked: boolean;
  onClick: () => void;
  title: React.ReactNode;
  description?: React.ReactNode;
  aside?: React.ReactNode;
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={checked}
      onClick={onClick}
      className={cn(
        'flex w-full items-start gap-3 rounded-lg border p-3.5 text-left transition-colors',
        checked ? 'border-accent bg-accent/[0.05] ring-1 ring-accent' : 'border-line bg-surface hover:border-line-strong'
      )}
    >
      <span
        className={cn(
          'mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded-full border',
          checked ? 'border-accent' : 'border-line-strong'
        )}
      >
        {checked && <span className="h-2 w-2 rounded-full bg-accent" />}
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex items-center justify-between gap-2">
          <span className="text-sm font-medium text-fg">{title}</span>
          {aside}
        </span>
        {description && <span className="mt-0.5 block text-[13px] text-fg-muted">{description}</span>}
      </span>
    </button>
  );
}

/* ───────────────────────── Tabs ───────────────────────── */

export function Tabs<T extends string>({
  items,
  value,
  onChange,
  className,
}: {
  items: { id: T; label: React.ReactNode; count?: number | string }[];
  value: T;
  onChange: (v: T) => void;
  className?: string;
}) {
  return (
    <div className={cn('scrollbar-none -mb-px flex gap-5 overflow-x-auto border-b border-line', className)} role="tablist">
      {items.map((t) => (
        <button
          key={t.id}
          role="tab"
          type="button"
          aria-selected={value === t.id}
          onClick={() => onChange(t.id)}
          className={cn(
            'relative flex h-10 shrink-0 items-center gap-1.5 text-sm font-medium transition-colors',
            value === t.id ? 'text-fg' : 'text-fg-muted hover:text-fg'
          )}
        >
          {t.label}
          {t.count !== undefined && (
            <span
              className={cn(
                'rounded-full px-1.5 text-xs tabular',
                value === t.id ? 'bg-fg text-canvas' : 'bg-muted text-fg-muted'
              )}
            >
              {t.count}
            </span>
          )}
          {value === t.id && <span className="absolute inset-x-0 -bottom-px h-0.5 rounded-full bg-fg" />}
        </button>
      ))}
    </div>
  );
}

/* ───────────────────────── Feedback ───────────────────────── */

export function Spinner({ className }: { className?: string }) {
  return <Loader2 className={cn('h-4 w-4 animate-spin text-fg-subtle', className)} />;
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={cn('animate-pulse rounded-md bg-muted', className)} />;
}

export function EmptyState({
  icon,
  title,
  description,
  action,
  className,
}: {
  icon?: React.ReactNode;
  title: React.ReactNode;
  description?: React.ReactNode;
  action?: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={cn('flex flex-col items-center justify-center px-6 py-14 text-center', className)}>
      {icon && (
        <div className="mb-4 flex h-10 w-10 items-center justify-center rounded-lg border border-line bg-subtle text-fg-subtle [&>svg]:h-5 [&>svg]:w-5">
          {icon}
        </div>
      )}
      <p className="text-sm font-medium text-fg">{title}</p>
      {description && <p className="mt-1 max-w-sm text-[13px] text-fg-muted">{description}</p>}
      {action && <div className="mt-5">{action}</div>}
    </div>
  );
}

export function ProgressBar({ value, tone = 'info', className }: { value: number; tone?: Tone; className?: string }) {
  const color: Record<Tone, string> = {
    neutral: 'bg-fg-subtle',
    info: 'bg-accent',
    success: 'bg-success',
    warning: 'bg-warning',
    danger: 'bg-danger',
  };
  return (
    <div className={cn('h-1.5 w-full overflow-hidden rounded-full bg-muted', className)}>
      <div
        className={cn('h-full rounded-full transition-[width] duration-500', color[tone])}
        style={{ width: `${Math.max(0, Math.min(100, value))}%` }}
      />
    </div>
  );
}

export function Kbd({ children }: { children: React.ReactNode }) {
  return (
    <kbd className="inline-flex h-5 min-w-[20px] items-center justify-center rounded border border-line bg-subtle px-1 font-mono text-[11px] text-fg-subtle">
      {children}
    </kbd>
  );
}

/* ───────────────────────── Image with fallback ───────────────────────── */

export function Thumb({
  src,
  alt = '',
  className,
  fallback,
}: {
  src?: string | null;
  alt?: string;
  className?: string;
  fallback?: React.ReactNode;
}) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [src]);
  return (
    <div className={cn('relative overflow-hidden bg-muted', className)}>
      {src && !failed ? (
        <img
          src={src}
          alt={alt}
          loading="lazy"
          referrerPolicy="no-referrer"
          onError={() => setFailed(true)}
          className="h-full w-full object-cover"
        />
      ) : (
        <div className="flex h-full w-full items-center justify-center text-fg-subtle">
          {fallback ?? <ImageOff className="h-4 w-4" />}
        </div>
      )}
    </div>
  );
}

/* ───────────────────────── Overlays ───────────────────────── */

function useEscape(open: boolean, onClose: () => void) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    document.addEventListener('keydown', onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = prev;
    };
  }, [open, onClose]);
}

export function Modal({
  open,
  onClose,
  title,
  description,
  children,
  footer,
  size = 'md',
}: {
  open: boolean;
  onClose: () => void;
  title: React.ReactNode;
  description?: React.ReactNode;
  children?: React.ReactNode;
  footer?: React.ReactNode;
  size?: 'sm' | 'md' | 'lg';
}) {
  useEscape(open, onClose);
  if (!open) return null;
  const width = { sm: 'max-w-sm', md: 'max-w-lg', lg: 'max-w-2xl' }[size];
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center sm:items-center sm:p-4">
      <div className="absolute inset-0 animate-fade-in bg-black/40" onClick={onClose} />
      <div
        role="dialog"
        aria-modal="true"
        className={cn(
          'relative flex max-h-[92vh] w-full animate-scale-in flex-col rounded-t-xl border border-line bg-surface shadow-pop sm:rounded-xl',
          width
        )}
      >
        <div className="flex items-start justify-between gap-4 px-5 pb-2 pt-5">
          <div>
            <h2 className="text-base font-semibold text-fg">{title}</h2>
            {description && <p className="mt-1 text-[13px] text-fg-muted">{description}</p>}
          </div>
          <IconButton label="Đóng" size="sm" onClick={onClose} className="-mr-1.5 -mt-1">
            <X className="h-4 w-4" />
          </IconButton>
        </div>
        {children && <div className="overflow-y-auto px-5 py-3">{children}</div>}
        {footer && <div className="pb-safe flex justify-end gap-2 border-t border-line px-5 py-3.5">{footer}</div>}
      </div>
    </div>
  );
}

export function Drawer({
  open,
  onClose,
  title,
  subtitle,
  children,
  footer,
}: {
  open: boolean;
  onClose: () => void;
  title: React.ReactNode;
  subtitle?: React.ReactNode;
  children: React.ReactNode;
  footer?: React.ReactNode;
}) {
  useEscape(open, onClose);
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-40">
      <div className="absolute inset-0 animate-fade-in bg-black/30" onClick={onClose} />
      <aside
        role="dialog"
        aria-modal="true"
        className="absolute inset-y-0 right-0 flex w-full max-w-[680px] animate-slide-in flex-col border-l border-line bg-surface shadow-pop"
      >
        <div className="flex items-start justify-between gap-4 border-b border-line px-4 py-4 sm:px-6">
          <div className="min-w-0">
            {subtitle && <div className="mb-1 text-xs text-fg-muted">{subtitle}</div>}
            <h2 className="text-base font-semibold leading-snug text-fg">{title}</h2>
          </div>
          <IconButton label="Đóng" size="sm" onClick={onClose}>
            <X className="h-4 w-4" />
          </IconButton>
        </div>
        <div className="flex-1 overflow-y-auto px-4 py-5 sm:px-6">{children}</div>
        {footer && <div className="pb-safe flex justify-end gap-2 border-t border-line px-4 py-3 sm:px-6 sm:py-3.5">{footer}</div>}
      </aside>
    </div>
  );
}

/* ───────────────────────── Dropdown menu ───────────────────────── */

export interface MenuItem {
  label: string;
  icon?: React.ReactNode;
  onSelect?: () => void;
  href?: string;
  download?: string;
  external?: boolean;
  danger?: boolean;
  disabled?: boolean;
  divider?: boolean;
}

export function Menu({ trigger, items, align = 'right' }: { trigger: React.ReactElement; items: MenuItem[]; align?: 'left' | 'right' }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  return (
    <div className="relative" ref={ref}>
      {React.cloneElement(trigger, { onClick: () => setOpen((o) => !o), 'aria-expanded': open })}
      {open && (
        <div
          className={cn(
            'absolute z-30 mt-1.5 min-w-[200px] animate-scale-in rounded-lg border border-line bg-surface p-1 shadow-pop',
            align === 'right' ? 'right-0' : 'left-0'
          )}
          role="menu"
        >
          {items.map((item, i) =>
            item.divider ? (
              <div key={i} className="my-1 h-px bg-line" />
            ) : item.href ? (
              <a
                key={i}
                href={item.href}
                download={item.download}
                target={item.external ? '_blank' : undefined}
                rel={item.external ? 'noreferrer' : undefined}
                onClick={() => setOpen(false)}
                className={cn(
                  'flex h-8 items-center gap-2.5 rounded-md px-2.5 text-[13px] [&>svg]:h-4 [&>svg]:w-4',
                  item.danger ? 'text-danger hover:bg-danger/10' : 'text-fg hover:bg-muted'
                )}
              >
                {item.icon}
                {item.label}
              </a>
            ) : (
              <button
                key={i}
                type="button"
                disabled={item.disabled}
                onClick={() => {
                  setOpen(false);
                  item.onSelect?.();
                }}
                className={cn(
                  'flex h-8 w-full items-center gap-2.5 rounded-md px-2.5 text-left text-[13px] disabled:opacity-50 [&>svg]:h-4 [&>svg]:w-4',
                  item.danger ? 'text-danger hover:bg-danger/10' : 'text-fg hover:bg-muted'
                )}
              >
                {item.icon}
                {item.label}
              </button>
            )
          )}
        </div>
      )}
    </div>
  );
}
