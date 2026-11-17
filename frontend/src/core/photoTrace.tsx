import { useCallback, useEffect, useState } from "react";

/**
 * TEMPORARY: a visible step-by-step log of picking a photo, to find out where it stops on iPhones
 * (desktop works; nothing visible happens on iOS). Remove once the cause is known.
 */
export function usePhotoTrace() {
  const [lines, setLines] = useState<string[]>([]);
  const add = useCallback((message: string) => {
    const t = new Date();
    const stamp = `${t.toLocaleTimeString([], { hour12: false })}.${String(t.getMilliseconds()).padStart(3, "0")}`;
    setLines((l) => [...l.slice(-39), `${stamp} ${message}`]);
  }, []);

  // Leaving for the camera or library hides the page; whether it comes back (or reloads) matters.
  useEffect(() => {
    add(`Ready. ${navigator.userAgent.match(/OS [\d_]+|Version\/[\d.]+/g)?.join(" ") ?? navigator.userAgent.slice(0, 60)}`
      + ((navigator as Navigator & { standalone?: boolean }).standalone ? " (home-screen app)" : ""));
    const onVisibility = () => add(`Page ${document.visibilityState}`);
    const onFocus = () => add("Window focused");
    const onBlur = () => add("Window lost focus");
    const onPageShow = (e: PageTransitionEvent) => add(`Page shown${e.persisted ? " (restored)" : ""}`);
    // Every tap anywhere, and any file input's change, with what received it: shows where a tap really lands
    // and whether a picked photo goes to an unexpected input.
    const describe = (t: EventTarget | null) => {
      if (!(t instanceof Element)) return String(t);
      const text = (t.textContent ?? "").trim().slice(0, 20);
      return `<${t.tagName.toLowerCase()}${t instanceof HTMLInputElement ? ` type=${t.type}` : ""}>${text ? ` "${text}"` : ""}`;
    };
    const onTap = (e: Event) => add(`${e.type} on ${describe(e.target)}`);
    const onAnyChange = (e: Event) => {
      if (e.target instanceof HTMLInputElement && e.target.type === "file")
        add(`Some file input changed: ${e.target.files?.length ?? 0} file(s)`);
    };
    document.addEventListener("visibilitychange", onVisibility);
    window.addEventListener("focus", onFocus);
    window.addEventListener("blur", onBlur);
    window.addEventListener("pageshow", onPageShow);
    document.addEventListener("pointerdown", onTap, true);
    document.addEventListener("click", onTap, true);
    document.addEventListener("change", onAnyChange, true);
    return () => {
      document.removeEventListener("visibilitychange", onVisibility);
      window.removeEventListener("focus", onFocus);
      window.removeEventListener("blur", onBlur);
      window.removeEventListener("pageshow", onPageShow);
      document.removeEventListener("pointerdown", onTap, true);
      document.removeEventListener("click", onTap, true);
      document.removeEventListener("change", onAnyChange, true);
    };
  }, [add]);

  return { lines, add };
}

export function PhotoTrace({ lines }: { lines: string[] }) {
  if (lines.length === 0) return null;
  return (
    <div className="rounded-xl border border-dashed border-border bg-surface-2 p-2">
      <p className="mb-1 text-[11px] font-semibold text-muted">Photo debug log (temporary). Screenshot this after trying.</p>
      <pre className="whitespace-pre-wrap break-all font-mono text-[11px] leading-snug text-text">{lines.join("\n")}</pre>
    </div>
  );
}
