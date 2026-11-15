import React, { createContext, useCallback, useContext, useRef, useState } from 'react';
import { CheckCircle2, AlertCircle, Info, X } from 'lucide-react';
import { Button, Modal, cn } from './ui';

/* ───────────────────────── Toasts ───────────────────────── */

type ToastKind = 'success' | 'error' | 'info';
interface ToastItem {
  id: number;
  kind: ToastKind;
  message: string;
  detail?: string;
}

interface ToastApi {
  success: (message: string, detail?: string) => void;
  error: (message: string, detail?: string) => void;
  info: (message: string, detail?: string) => void;
}

const ToastContext = createContext<ToastApi | null>(null);

export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast must be used inside FeedbackProvider');
  return ctx;
}

/* ───────────────────────── Confirm ───────────────────────── */

interface ConfirmOptions {
  title: string;
  description?: string;
  confirmLabel?: string;
  danger?: boolean;
}

const ConfirmContext = createContext<((o: ConfirmOptions) => Promise<boolean>) | null>(null);

export function useConfirm() {
  const ctx = useContext(ConfirmContext);
  if (!ctx) throw new Error('useConfirm must be used inside FeedbackProvider');
  return ctx;
}

export function FeedbackProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => setToasts((t) => t.filter((x) => x.id !== id)), []);

  const push = useCallback(
    (kind: ToastKind, message: string, detail?: string) => {
      const id = nextId.current++;
      setToasts((t) => [...t.slice(-3), { id, kind, message, detail }]);
      setTimeout(() => dismiss(id), kind === 'error' ? 7000 : 4000);
    },
    [dismiss]
  );

  const api = useRef<ToastApi>({
    success: (m, d) => push('success', m, d),
    error: (m, d) => push('error', m, d),
    info: (m, d) => push('info', m, d),
  });
  api.current.success = (m, d) => push('success', m, d);
  api.current.error = (m, d) => push('error', m, d);
  api.current.info = (m, d) => push('info', m, d);

  const [confirmState, setConfirmState] = useState<(ConfirmOptions & { resolve: (v: boolean) => void }) | null>(null);
  const confirm = useCallback(
    (o: ConfirmOptions) => new Promise<boolean>((resolve) => setConfirmState({ ...o, resolve })),
    []
  );
  const closeConfirm = (value: boolean) => {
    confirmState?.resolve(value);
    setConfirmState(null);
  };

  const icon = {
    success: <CheckCircle2 className="h-4 w-4 text-success" />,
    error: <AlertCircle className="h-4 w-4 text-danger" />,
    info: <Info className="h-4 w-4 text-accent" />,
  };

  return (
    <ToastContext.Provider value={api.current}>
      <ConfirmContext.Provider value={confirm}>
        {children}

        <Modal
          open={!!confirmState}
          onClose={() => closeConfirm(false)}
          title={confirmState?.title}
          description={confirmState?.description}
          size="sm"
          footer={
            <>
              <Button variant="ghost" onClick={() => closeConfirm(false)}>
                Huỷ
              </Button>
              <Button variant={confirmState?.danger ? 'danger' : 'primary'} onClick={() => closeConfirm(true)} autoFocus>
                {confirmState?.confirmLabel ?? 'Xác nhận'}
              </Button>
            </>
          }
        />

        <div className="pointer-events-none fixed bottom-[calc(5rem+env(safe-area-inset-bottom))] right-4 z-[60] flex w-[calc(100%-2rem)] max-w-sm flex-col gap-2 lg:bottom-4">
          {toasts.map((t) => (
            <div
              key={t.id}
              role="status"
              className={cn(
                'pointer-events-auto flex animate-scale-in items-start gap-3 rounded-lg border border-line bg-surface px-4 py-3 shadow-pop'
              )}
            >
              <span className="mt-0.5 shrink-0">{icon[t.kind]}</span>
              <div className="min-w-0 flex-1">
                <p className="text-sm text-fg">{t.message}</p>
                {t.detail && <p className="mt-0.5 break-words text-xs text-fg-muted">{t.detail}</p>}
              </div>
              <button
                onClick={() => dismiss(t.id)}
                className="-mr-1 shrink-0 rounded p-0.5 text-fg-subtle hover:text-fg"
                aria-label="Đóng thông báo"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            </div>
          ))}
        </div>
      </ConfirmContext.Provider>
    </ToastContext.Provider>
  );
}
