import { useEffect, useRef, type ButtonHTMLAttributes, type ComponentProps, type PointerEvent, type ReactNode } from "react";
import { X, Loader2 } from "lucide-react";
import { haptic, reducedMotion, usePresence } from "./motion";

const cx = (...c: (string | false | null | undefined)[]) => c.filter(Boolean).join(" ");
export { cx };

type Variant = "primary" | "secondary" | "ghost" | "danger";
const variants: Record<Variant, string> = {
  primary: "bg-accent text-accent-text hover:opacity-90",
  secondary: "bg-surface-2 text-text hover:bg-border",
  ghost: "text-text hover:bg-surface-2",
  danger: "bg-danger-soft text-danger hover:opacity-90",
};

const buttonBase = "press inline-flex min-h-11 items-center justify-center gap-2 rounded-xl px-4 text-sm font-medium disabled:opacity-50";

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

/**
 * A tick box that feels like a native control: a 44px tap target, a little pop and a drawn tick when it's checked.
 * `round` for list check-offs (shopping), square for everything else.
 */
export function Check({ checked, onChange, label, disabled, round, animate, className = "-m-2.5 size-11" }: {
  checked: boolean; onChange: (checked: boolean) => void; label: string; disabled?: boolean; round?: boolean;
  /** Play the tick animation even though it was already checked when it appeared (it moved to another list). */
  animate?: boolean;
  /** Sizing; replaces the default 44px target that overlaps its neighbours to look 24px. */
  className?: string;
}) {
  return (
    <button type="button" role="checkbox" aria-checked={checked} aria-label={label} disabled={disabled}
      onClick={() => { haptic(); onChange(!checked); }}
      className={cx("press grid shrink-0 place-items-center disabled:opacity-50", className)}>
      <TickBox checked={checked} round={round} animate={animate} />
    </button>
  );
}

/** Just the box of a `Check`, for rows that are one big tap target themselves (call `haptic()` in their onClick). */
export function TickBox({ checked, round, animate: force, className }: { checked: boolean; round?: boolean; animate?: boolean; className?: string }) {
  // Only animate changes made while on screen, not every checked box on page load.
  const initial = useRef(checked);
  const animate = checked && (!initial.current || !!force);
  if (!checked) initial.current = false;
  return (
    <span className={cx("grid size-6 shrink-0 place-items-center border-2 transition-colors duration-200",
      round ? "rounded-full" : "rounded-lg",
      checked ? "border-accent bg-accent text-accent-text" : "border-border bg-surface",
      animate && "animate-pop", className)}>
      {checked && (
        <svg viewBox="0 0 24 24" className="size-4" fill="none" stroke="currentColor" strokeWidth={3.2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
          <path d="M5 12.5l4.5 4.5L19 7.5" strokeDasharray={24} className={animate ? "animate-draw" : undefined} />
        </svg>
      )}
    </span>
  );
}

const SHEET_EXIT_MS = 220;

/**
 * Bottom sheet on phones, centered dialog on larger screens. Slides up, and on phones can be swiped down by its
 * header to close. It keeps showing its last content while it animates away, so callers can clear their state
 * as soon as it closes.
 */
export function Sheet({ open, onClose, title, children, footer }: { open: boolean; onClose: () => void; title: ReactNode; children: ReactNode; footer?: ReactNode }) {
  const { mounted, leaving } = usePresence(open, SHEET_EXIT_MS);
  const last = useRef({ title, children, footer });
  if (open) last.current = { title, children, footer };
  const shown = open ? { title, children, footer } : last.current;
  const panel = useRef<HTMLDivElement>(null);
  const drag = useRef<{ y: number; t: number; dy: number; v: number } | null>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { document.removeEventListener("keydown", onKey); document.body.style.overflow = overflow; };
  }, [open, onClose]);

  // A fresh open starts from the bottom, not wherever the last swipe left it.
  useEffect(() => { if (open && panel.current) panel.current.style.transform = ""; }, [open]);

  if (!mounted) return null;

  // Swipe to close: follow the finger down (resisting upward pulls); let go far or fast enough and it closes.
  const isPhone = () => !window.matchMedia("(min-width: 640px)").matches;
  const onDown = (e: PointerEvent) => {
    if (!isPhone() || (e.target as HTMLElement).closest("button")) return;
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    drag.current = { y: e.clientY, t: performance.now(), dy: 0, v: 0 };
  };
  const onMove = (e: PointerEvent) => {
    const d = drag.current;
    if (!d || !panel.current) return;
    const dy = e.clientY - d.y;
    const now = performance.now();
    d.v = (dy - d.dy) / Math.max(1, now - d.t);
    d.t = now; d.dy = dy;
    panel.current.style.transition = "none";
    panel.current.style.transform = `translateY(${dy > 0 ? dy : -Math.sqrt(-dy) * 2}px)`;
  };
  const onUp = () => {
    const d = drag.current;
    drag.current = null;
    if (!d || !panel.current) return;
    if (d.dy > 110 || (d.dy > 24 && d.v > 0.5)) { haptic(); onClose(); return; }
    panel.current.style.transition = reducedMotion() ? "none" : "transform 320ms cubic-bezier(0.22, 1, 0.36, 1)";
    panel.current.style.transform = "";
  };

  return (
    <div className={cx("fixed inset-0 z-50 flex items-end justify-center sm:items-center sm:p-6", leaving && "pointer-events-none")}
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div aria-hidden className={cx("absolute inset-0 bg-black/40", leaving ? "animate-fade-out" : "animate-fade-in")}
        onMouseDown={onClose} />
      <div ref={panel} role="dialog" aria-modal="true"
        className={cx("relative flex max-h-[92dvh] w-full flex-col rounded-t-3xl bg-surface shadow-xl sm:max-w-lg sm:rounded-3xl",
          leaving ? "animate-sheet-out sm:animate-dialog-out" : "animate-sheet-in sm:animate-dialog-in")}>
        <div className="touch-none select-none" onPointerDown={onDown} onPointerMove={onMove} onPointerUp={onUp} onPointerCancel={onUp}>
          <div className="flex justify-center pt-2 sm:hidden"><span className="h-1 w-10 rounded-full bg-border" /></div>
          <div className="flex items-center justify-between gap-2 border-b border-border px-4 pb-3 pt-1 sm:pt-3">
            <h2 className="min-w-0 truncate text-base font-semibold">{shown.title}</h2>
            <button onClick={onClose} className="press rounded-full p-2 text-muted hover:bg-surface-2" aria-label="Close"><X className="size-5" /></button>
          </div>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 py-4">{shown.children}</div>
        {shown.footer && <div className="pb-safe border-t border-border px-4 py-3">{shown.footer}</div>}
      </div>
    </div>
  );
}

export function ErrorText({ error }: { error: unknown }) {
  if (!error) return null;
  return <p className="rounded-xl bg-danger-soft px-3 py-2 text-sm text-danger">{error instanceof Error ? error.message : String(error)}</p>;
}
