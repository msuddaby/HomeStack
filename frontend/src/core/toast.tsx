import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { X } from "lucide-react";
import { cx } from "./ui";

export interface ToastOptions {
  message: string;
  /** e.g. { label: "Undo", onClick } */
  action?: { label: string; onClick: () => void | Promise<unknown> };
  tone?: "neutral" | "danger";
}

const ToastContext = createContext<(t: ToastOptions) => void>(() => {});

/** Show a short message at the bottom of the screen; a new one replaces the current one. */
export const useToast = () => useContext(ToastContext);

const SHOW_MS = 6000;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<(ToastOptions & { key: number }) | null>(null);
  const [busy, setBusy] = useState(false);
  const timer = useRef<number>(undefined);

  const show = useCallback((t: ToastOptions) => { setBusy(false); setToast({ ...t, key: Date.now() }); }, []);
  const hide = useCallback(() => setToast(null), []);

  useEffect(() => {
    if (!toast) return;
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(hide, SHOW_MS);
    return () => window.clearTimeout(timer.current);
  }, [toast, hide]);

  const runAction = async () => {
    if (!toast?.action) return;
    setBusy(true);
    window.clearTimeout(timer.current);
    try {
      await toast.action.onClick();
      hide();
    } catch (e) {
      show({ message: e instanceof Error ? e.message : String(e), tone: "danger" });
    }
  };

  return (
    <ToastContext.Provider value={show}>
      {children}
      {/* Above the phone tab bar and any open sheet. */}
      <div aria-live="polite" className="pointer-events-none fixed inset-x-0 bottom-24 z-[60] flex justify-center px-4 md:bottom-6">
        {toast && (
          <div key={toast.key} role="status"
            className={cx("pointer-events-auto flex w-full max-w-md items-center gap-2 rounded-2xl py-2 pl-4 pr-2 text-sm shadow-lg",
              toast.tone === "danger" ? "bg-danger text-surface" : "bg-text text-surface")}>
            <span className="min-w-0 flex-1">{toast.message}</span>
            {toast.action && (
              <button onClick={runAction} disabled={busy}
                className="min-h-11 rounded-xl px-3 font-semibold text-accent-soft hover:bg-white/10 disabled:opacity-50">
                {busy ? "…" : toast.action.label}
              </button>
            )}
            <button onClick={hide} aria-label="Dismiss" className="rounded-full p-2 opacity-70 hover:opacity-100"><X className="size-4" /></button>
          </div>
        )}
      </div>
    </ToastContext.Provider>
  );
}
