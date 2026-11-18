import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode, type RefObject } from "react";

/** Animation helpers. The look (curves, keyframes) lives in index.css under "Motion"; these handle timing and state. */

export const EASE_OUT = "cubic-bezier(0.22, 1, 0.36, 1)";

/** The person asked their phone for less motion. */
export const reducedMotion = () => typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/** A tiny buzz on phones that support it (Android). iPhones ignore it. */
export function haptic(ms = 8) {
  try { navigator.vibrate?.(ms); } catch { /* not allowed here */ }
}

/**
 * Keeps something on screen for `exitMs` after `open` turns false, so it can play its exit animation.
 * `leaving` is true during that time.
 */
export function usePresence(open: boolean, exitMs: number) {
  const [mounted, setMounted] = useState(open);
  if (open && !mounted) setMounted(true);
  useEffect(() => {
    if (open || !mounted) return;
    const t = window.setTimeout(() => setMounted(false), reducedMotion() ? 0 : exitMs);
    return () => window.clearTimeout(t);
  }, [open, mounted, exitMs]);
  return { mounted: mounted || open, leaving: mounted && !open };
}

type Key = string | number;

/**
 * A list that lets removed items leave gracefully: an item that drops out of `items` stays where it was for
 * `exitMs`, flagged `leaving` (give it `animate-collapse`), then goes. Items that come back are restored.
 */
export function useExitingList<T>(items: T[], key: (t: T) => Key, exitMs = 280): { item: T; key: Key; leaving: boolean; entering: boolean }[] {
  // What was there when the list first showed up doesn't animate in; anything added (or brought back) later does.
  const base = useRef<Set<Key> | null>(null);
  if (!base.current && items.length) base.current = new Set(items.map(key));
  const [gone, setGone] = useState<Map<Key, { item: T; after: Key | null }>>(() => new Map());
  const prev = useRef(items);
  const keyRef = useRef(key);
  keyRef.current = key;

  useLayoutEffect(() => {
    const k = keyRef.current;
    const now = new Set(items.map(k));
    const left = prev.current.flatMap((item, i) => (now.has(k(item)) ? [] : [{ item, after: i > 0 ? k(prev.current[i - 1]!) : null }]));
    prev.current = items;
    for (const l of left) base.current?.delete(k(l.item));
    setGone((g) => {
      const next = new Map([...g].filter(([id]) => !now.has(id)));
      for (const l of left) next.set(k(l.item), l);
      return next.size === g.size && [...next.keys()].every((id) => g.has(id)) ? g : next;
    });
    if (!left.length) return;
    const ids = left.map((l) => k(l.item));
    const t = window.setTimeout(() => setGone((g) => {
      const next = new Map(g);
      ids.forEach((id) => next.delete(id));
      return next;
    }), reducedMotion() ? 0 : exitMs);
    return () => window.clearTimeout(t);
  }, [items, exitMs]);

  const out = items.map((item) => ({ item, key: key(item), leaving: false, entering: !!base.current && !base.current.has(key(item)) }));
  // Put each leaving item back after the one it used to follow (or at the top).
  for (const [id, g] of gone) {
    if (out.some((o) => o.key === id)) continue;
    const at = g.after == null ? 0 : out.findIndex((o) => o.key === g.after) + 1;
    out.splice(at <= 0 && g.after != null ? out.length : at, 0, { item: g.item, key: id, leaving: true, entering: false });
  }
  return out;
}

/**
 * A short grace period: `hold(id)` marks something for `ms`. Use it to keep a just-ticked item visible
 * (showing its new state) before it leaves the list.
 */
export function useHold(ms: number) {
  const [held, setHeld] = useState<Set<Key>>(() => new Set());
  const timers = useRef(new Map<Key, number>());
  useEffect(() => () => timers.current.forEach((t) => window.clearTimeout(t)), []);
  const release = useCallback((id: Key) => {
    window.clearTimeout(timers.current.get(id));
    timers.current.delete(id);
    setHeld((h) => { const n = new Set(h); n.delete(id); return n; });
  }, []);
  const hold = useCallback((id: Key) => {
    window.clearTimeout(timers.current.get(id));
    setHeld((h) => new Set(h).add(id));
    timers.current.set(id, window.setTimeout(() => release(id), reducedMotion() ? 0 : ms));
  }, [ms, release]);
  return { hold, release, isHeld: (id: Key) => held.has(id) };
}

/**
 * Animates elements marked `data-flip="<id>"` inside `ref` from where they were to where they are now, whenever
 * the component re-renders and they've moved (FLIP). New ones fade in. Elements can change parents (e.g. move
 * from one section to another) as long as the id stays the same.
 */
export function useFlip(ref: RefObject<HTMLElement | null>) {
  const last = useRef<Map<string, { x: number; y: number }> | null>(null);
  useLayoutEffect(() => {
    const root = ref.current;
    if (!root) return;
    const els = [...root.querySelectorAll<HTMLElement>("[data-flip]")];
    els.forEach((el) => el.getAnimations().forEach((a) => a.id === "flip" && a.cancel()));
    const o = root.getBoundingClientRect();
    const now = new Map<string, { x: number; y: number }>();
    for (const el of els) {
      const r = el.getBoundingClientRect();
      now.set(el.dataset.flip!, { x: r.left - o.left, y: r.top - o.top });
    }
    const before = last.current;
    last.current = now;
    if (!before || reducedMotion()) return;
    for (const el of els) {
      const p = now.get(el.dataset.flip!)!;
      const was = before.get(el.dataset.flip!);
      if (!was) {
        el.animate([{ opacity: 0, transform: "translateY(6px)" }, { opacity: 1, transform: "none" }], { duration: 260, easing: EASE_OUT, id: "flip" });
        continue;
      }
      const dx = was.x - p.x, dy = was.y - p.y;
      if (Math.abs(dx) < 1 && Math.abs(dy) < 1) continue;
      // Long trips take a little longer, so they read as movement rather than a jump.
      const duration = Math.min(560, 300 + Math.hypot(dx, dy) * 0.25);
      el.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: "none" }], { duration, easing: EASE_OUT, id: "flip" });
    }
  });
}

/** A number that counts to its new value instead of snapping. */
export function AnimatedNumber({ value, format, className }: { value: number; format: (n: number) => string; className?: string }) {
  const [shown, setShown] = useState(value);
  const from = useRef(value);
  useEffect(() => {
    if (from.current === value) return;
    if (reducedMotion()) { from.current = value; setShown(value); return; }
    const start = performance.now(), a = from.current, ms = 450;
    let raf = 0;
    const tick = (t: number) => {
      const p = Math.min(1, (t - start) / ms);
      const v = a + (value - a) * (1 - Math.pow(1 - p, 3));
      from.current = v;
      setShown(p === 1 ? value : v);
      if (p < 1) raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [value]);
  return <span className={className}>{format(shown)}</span>;
}

/**
 * Wraps a list entry so it can collapse away (`leaving`) or open up (`entering`) without the rest of the list
 * jumping. Put padding and borders on the children, not here: the wrapper's own height is what animates.
 */
export function MotionItem({ as: As = "div", leaving, entering, className, children }: {
  as?: "div" | "li"; leaving?: boolean; entering?: boolean; className?: string; children: ReactNode;
}) {
  const cls = ["motion-rows", leaving && "animate-collapse pointer-events-none", entering && "animate-expand", className].filter(Boolean).join(" ");
  return <As className={cls} aria-hidden={leaving || undefined}><div>{children}</div></As>;
}

/**
 * Keeps a list in the order it was first shown while you're on the page, so things don't reshuffle under your
 * finger when a refetch sorts them differently (e.g. by amount). New items slot in where the server put them
 * relative to what's already there; the next visit gets the server's order afresh.
 */
export function useStableOrder<T>(items: T[], key: (t: T) => Key): T[] {
  const rank = useRef(new Map<Key, number>());
  const r = rank.current;
  let prevRank = -1;
  for (const item of items) {
    const k = key(item);
    const known = r.get(k);
    if (known != null) { prevRank = known; continue; }
    // Just after whatever came before it in the server's order.
    const next = prevRank + 0.001;
    r.set(k, next);
    prevRank = next;
  }
  return [...items].sort((a, b) => r.get(key(a))! - r.get(key(b))!);
}
