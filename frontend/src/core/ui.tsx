import { useEffect, useRef, type ButtonHTMLAttributes, type ComponentProps, type ReactNode } from "react";
import { X, Loader2 } from "lucide-react";

const cx = (...c: (string | false | null | undefined)[]) => c.filter(Boolean).join(" ");
export { cx };

type Variant = "primary" | "secondary" | "ghost" | "danger";
const variants: Record<Variant, string> = {
  primary: "bg-accent text-accent-text hover:opacity-90",
  secondary: "bg-surface-2 text-text hover:bg-border",
  ghost: "text-text hover:bg-surface-2",
  danger: "bg-danger-soft text-danger hover:opacity-90",
};

const buttonBase = "inline-flex min-h-11 items-center justify-center gap-2 rounded-xl px-4 text-sm font-medium transition disabled:opacity-50";

export function Button({ variant = "secondary", busy, className, children, disabled, ...rest }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; busy?: boolean }) {
  return (
    <button
      {...rest}
      disabled={disabled || busy}
      className={cx(buttonBase, variants[variant], className)}
    >
      {busy && <Loader2 className="size-4 animate-spin" />}
      {children}
    </button>
  );
}

/**
 * A button that opens the phone's camera / photo library. The real file input is stretched invisibly over the
 * whole button, so the tap lands on the input itself. Don't change this to a hidden input clicked from code or a
 * label: on iPhones (home-screen app, iOS 18) the tap then never reaches the input. Confirmed with an on-screen log.
 * The input is only cleared after `onFiles` finishes, because iOS can discard a picked photo's data once it is,
 * so callers should read or copy the files before returning.
 */
export function PhotoButton({ onFiles, busy, multiple, camera, accept = "image/*", variant = "secondary", bare, className, children }: {
  onFiles: (files: File[]) => Promise<void>; busy?: boolean; multiple?: boolean;
  /** Open the camera straight away instead of offering the library. */
  camera?: boolean;
  /** File types offered, e.g. "application/pdf,image/*" for bills. */
  accept?: string;
  variant?: Variant;
  /** Skip the standard button look, for custom-styled tiles. */
  bare?: boolean;
  className?: string; children: ReactNode;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  // Kept current so the native listeners below always call the latest callback.
  const latest = useRef(onFiles);
  latest.current = onFiles;

  // iOS sends "input", "change" or both when a photo is picked; handle whichever arrives, once.
  useEffect(() => {
    const input = inputRef.current;
    if (!input) return;
    let handling = false;
    const handle = async () => {
      const files = Array.from(input.files ?? []);
      if (files.length === 0 || handling) return;
      handling = true;
      try { await latest.current(files); } finally { input.value = ""; handling = false; }
    };
    input.addEventListener("change", handle);
    input.addEventListener("input", handle);
    return () => {
      input.removeEventListener("change", handle);
      input.removeEventListener("input", handle);
    };
  }, []);

  return (
    <span className={cx("relative", !bare && buttonBase, !bare && variants[variant], busy && "pointer-events-none opacity-50", className)}>
      {busy && <Loader2 className="size-4 animate-spin" />}
      {children}
      <input ref={inputRef} type="file" accept={accept} multiple={multiple} capture={camera ? "environment" : undefined}
        disabled={busy} aria-label={accept === "image/*" ? "Choose a photo" : "Choose a file"} className="absolute inset-0 size-full cursor-pointer opacity-0" />
    </span>
  );
}

export function Field({ label, error, hint, children, className }: { label: string; error?: string; hint?: ReactNode; children: ReactNode; className?: string }) {
  return (
    // min-w-0: in a grid, let the field shrink to its column instead of its content's natural width.
    <label className={cx("block min-w-0", className)}>
      <span className="mb-1 block text-xs font-medium text-muted">{label}</span>
      {children}
      {error ? <span className="mt-1 block text-xs text-danger">{error}</span>
        : hint ? <span className="mt-1 block text-xs text-muted">{hint}</span> : null}
    </label>
  );
}

const controlBase = "min-h-11 rounded-xl border border-border bg-surface px-3 text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent/25";

/** Full width unless the caller sets its own width (Tailwind can't tell w-full and w-28 apart by order). */
function control(className?: string) {
  return cx(controlBase, !/(^|\s)(w|flex|basis)-/.test(className ?? "") && "w-full", className);
}

export function Input({ className, ...rest }: ComponentProps<"input">) {
  return <input {...rest} className={control(className)} />;
}

export function Select({ className, children, ...rest }: ComponentProps<"select">) {
  return <select {...rest} className={cx(control(className), "appearance-auto")}>{children}</select>;
}

export function Card({ title, action, children, className }: { title?: ReactNode; action?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={cx("rounded-2xl border border-border bg-surface p-4", className)}>
      {(title || action) && (
        <div className="mb-3 flex items-center justify-between gap-2">
          <h2 className="text-sm font-semibold">{title}</h2>
          {action}
        </div>
      )}
      {children}
    </section>
  );
}

export type Tone = "neutral" | "danger" | "warn" | "frost" | "accent";
const tones: Record<Tone, string> = {
  neutral: "bg-surface-2 text-muted",
  danger: "bg-danger-soft text-danger",
  warn: "bg-warn-soft text-warn",
  frost: "bg-frost-soft text-frost",
  accent: "bg-accent-soft text-accent",
};
export function Chip({ tone = "neutral", children, className }: { tone?: Tone; children: ReactNode; className?: string }) {
  return <span className={cx("inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium", tones[tone], className)}>{children}</span>;
}

export function Spinner() {
  return <div className="flex justify-center p-8"><Loader2 className="size-6 animate-spin text-muted" /></div>;
}

export function Empty({ icon, title, children }: { icon?: ReactNode; title: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-2 px-6 py-12 text-center">
      {icon && <div className="text-muted">{icon}</div>}
      <p className="font-medium">{title}</p>
      {children && <div className="text-sm text-muted">{children}</div>}
    </div>
  );
}

/** Bottom sheet on phones, centered dialog on larger screens. */
export function Sheet({ open, onClose, title, children, footer }: { open: boolean; onClose: () => void; title: ReactNode; children: ReactNode; footer?: ReactNode }) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { document.removeEventListener("keydown", onKey); document.body.style.overflow = overflow; };
  }, [open, onClose]);

  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/40 sm:items-center sm:p-6" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div role="dialog" aria-modal="true" className="flex max-h-[92dvh] w-full flex-col rounded-t-3xl bg-surface shadow-xl sm:max-w-lg sm:rounded-3xl">
        <div className="flex items-center justify-between gap-2 border-b border-border px-4 py-3">
          <h2 className="min-w-0 truncate text-base font-semibold">{title}</h2>
          <button onClick={onClose} className="rounded-full p-2 text-muted hover:bg-surface-2" aria-label="Close"><X className="size-5" /></button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4">{children}</div>
        {footer && <div className="pb-safe border-t border-border px-4 py-3">{footer}</div>}
      </div>
    </div>
  );
}

export function ErrorText({ error }: { error: unknown }) {
  if (!error) return null;
  return <p className="rounded-xl bg-danger-soft px-3 py-2 text-sm text-danger">{error instanceof Error ? error.message : String(error)}</p>;
}
