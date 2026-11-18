import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { ArrowLeft, ArrowRight, Check, ChefHat, ListChecks, Minus, Plus, Sun, Timer, X } from "lucide-react";
import { haptic, useSlideClass } from "@/core/motion";
import { Button, cx, Empty, Sheet, Spinner, TickBox } from "@/core/ui";
import { useRecipe, type Ingredient } from "./api";
import { CookSheet } from "./CookSheet";
import { prettyAmount } from "./format";
import { clock, findTimers, kitchenSteps, mentionedIngredients, type StepTimer } from "./kitchen";

interface RunningTimer { id: number; label: string; step: number; endsAt: number; done: boolean }

/** Remembers where you were if the page reloads mid-recipe (per browser tab). */
function useSaved<T>(key: string, initial: T): [T, (v: T) => void] {
  const [value, setValue] = useState<T>(() => {
    try { const s = sessionStorage.getItem(key); return s ? (JSON.parse(s) as T) : initial; } catch { return initial; }
  });
  const set = useCallback((v: T) => {
    setValue(v);
    try { sessionStorage.setItem(key, JSON.stringify(v)); } catch { /* storage unavailable: fine */ }
  }, [key]);
  return [value, set];
}

/** Keeps the screen on while cooking, where the browser allows it (re-requested when you come back to the tab). */
function useWakeLock() {
  const [on, setOn] = useState(false);
  useEffect(() => {
    if (!("wakeLock" in navigator)) return;
    let lock: WakeLockSentinel | null = null;
    let stopped = false;
    const request = async () => {
      try {
        lock = await navigator.wakeLock.request("screen");
        if (stopped) { lock.release(); return; }
        setOn(true);
        lock.addEventListener("release", () => setOn(false));
      } catch { setOn(false); }
    };
    const onVisible = () => document.visibilityState === "visible" && request();
    request();
    document.addEventListener("visibilitychange", onVisible);
    return () => { stopped = true; document.removeEventListener("visibilitychange", onVisible); lock?.release(); };
  }, []);
  return on;
}

/** A short beep. The AudioContext is created on a tap (starting a timer), as iPhones require. */
function useBeeper() {
  const ctx = useRef<AudioContext | null>(null);
  const prime = () => {
    try { ctx.current ??= new AudioContext(); void ctx.current.resume(); } catch { /* no audio: vibration still works */ }
  };
  const beep = () => {
    navigator.vibrate?.([300, 150, 300, 150, 300]);
    const c = ctx.current;
    if (!c) return;
    for (let i = 0; i < 3; i++) {
      const o = c.createOscillator(), g = c.createGain();
      o.frequency.value = 880;
      o.connect(g); g.connect(c.destination);
      const t = c.currentTime + i * 0.4;
      g.gain.setValueAtTime(0.25, t); g.gain.exponentialRampToValueAtTime(0.001, t + 0.3);
      o.start(t); o.stop(t + 0.3);
    }
  };
  return { prime, beep };
}

export function KitchenPage() {
  const id = Number(useParams().id);
  const recipe = useRecipe(id);
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [page, setPage] = useSaved(`kitchen:${id}:page`, 0);
  const [checked, setChecked] = useSaved<number[]>(`kitchen:${id}:checked`, []);
  const [servings, setServings] = useSaved<number | null>(`kitchen:${id}:servings`, Number(params.get("servings")) || null);
  // Timers store their end time, so a reload resumes them with the right time left.
  const [timers, setTimers] = useState<RunningTimer[]>(() => {
    try { return JSON.parse(sessionStorage.getItem(`kitchen:${id}:timers`) ?? "[]") as RunningTimer[]; } catch { return []; }
  });
  useEffect(() => {
    try { sessionStorage.setItem(`kitchen:${id}:timers`, JSON.stringify(timers)); } catch { /* storage unavailable: fine */ }
  }, [id, timers]);
  const [now, setNow] = useState(() => Date.now());
  const [showIngredients, setShowIngredients] = useState(false);
  const [cooking, setCooking] = useState(false);
  const awake = useWakeLock();
  const { prime, beep } = useBeeper();
  const touch = useRef<{ x: number; y: number } | null>(null);

  const r = recipe.data;
  const steps = useMemo(() => kitchenSteps(r?.steps ?? []), [r?.steps]);
  const last = steps.length + 1; // 0 = get ready, 1..n = steps, n+1 = done
  const current = Math.min(page, last);
  const go = useCallback((p: number) => setPage(Math.max(0, Math.min(last, p))), [last, setPage]);
  // Steps slide in from the side you're heading: forward from the right, back from the left.
  const slide = useSlideClass(current);

  // Tick while any timer runs; beep once when one finishes.
  const running = timers.some((t) => !t.done);
  const timersRef = useRef(timers);
  timersRef.current = timers;
  useEffect(() => {
    if (!running) return;
    const tick = () => {
      const t = Date.now();
      setNow(t);
      const ts = timersRef.current;
      if (ts.some((x) => !x.done && x.endsAt <= t)) {
        beep();
        setTimers(ts.map((x) => (!x.done && x.endsAt <= t ? { ...x, done: true } : x)));
      }
    };
    const h = window.setInterval(tick, 500);
    return () => window.clearInterval(h);
  }, [running]); // eslint-disable-line react-hooks/exhaustive-deps

  // While cooking, a sideways swipe means "next/previous step", not the browser's "go back" gesture.
  useEffect(() => {
    const els = [document.documentElement, document.body];
    const before = els.map((el) => el.style.overscrollBehaviorX);
    els.forEach((el) => (el.style.overscrollBehaviorX = "none"));
    return () => els.forEach((el, i) => (el.style.overscrollBehaviorX = before[i]!));
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (showIngredients || cooking) return;
      if (e.key === "ArrowRight") go(current + 1);
      if (e.key === "ArrowLeft") go(current - 1);
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [current, go, showIngredients, cooking]);

  if (recipe.isPending) return <Spinner />;
  if (recipe.error || !r) return <Empty title="Recipe not found"><Link to="/meals/recipes" className="text-accent">Back to recipes</Link></Empty>;

  const serves = servings ?? r.servings;
  const factor = serves / r.servings;
  const toggle = (ingredientId: number) =>
    setChecked(checked.includes(ingredientId) ? checked.filter((x) => x !== ingredientId) : [...checked, ingredientId]);
  const startTimer = (t: StepTimer, step: number) => {
    prime();
    setNow(Date.now());
    setTimers((ts) => [...ts, { id: Date.now(), label: t.label, step, endsAt: Date.now() + t.seconds * 1000, done: false }]);
  };
  const exit = () => navigate(`/meals/recipes/${r.id}`);
  const step = current >= 1 && current <= steps.length ? steps[current - 1]! : null;

  return (
    // touch-pan-y + overscroll-x-none: sideways swipes change step instead of triggering the browser's "back" gesture.
    <div className="fixed inset-0 z-50 flex touch-pan-y flex-col overscroll-x-none bg-bg"
      onTouchStart={(e) => { const t = e.touches[0]!; touch.current = { x: t.clientX, y: t.clientY }; }}
      onTouchEnd={(e) => {
        const start = touch.current, t = e.changedTouches[0]!;
        touch.current = null;
        if (!start || showIngredients || cooking) return;
        const dx = t.clientX - start.x, dy = t.clientY - start.y;
        if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5) go(current + (dx < 0 ? 1 : -1));
      }}>
      <header className="pt-safe border-b border-border bg-surface">
        <div className="mx-auto flex max-w-3xl items-center gap-2 px-2 py-2">
          <button onClick={exit} className="rounded-full p-2.5 text-muted hover:bg-surface-2" aria-label="Leave cook mode"><X className="size-6" /></button>
          <div className="min-w-0 flex-1">
            <p className="truncate font-semibold">{r.title}</p>
            <p className="flex items-center gap-1 text-xs text-muted">
              {current === 0 ? "Get ready" : step ? `Step ${step.number} of ${steps.length}` : "Done"}
              {awake && <><span aria-hidden>·</span><Sun className="size-3" />screen stays on</>}
            </p>
          </div>
          <Button variant="ghost" className="px-3" onClick={() => setShowIngredients(true)}><ListChecks className="size-5" />Ingredients</Button>
        </div>
        <div className="h-1 bg-surface-2">
          <div className="h-full bg-accent transition-[width] duration-500 ease-out-soft" style={{ width: `${(100 * current) / last}%` }} />
        </div>
      </header>

      {timers.length > 0 && (
        <div className="border-b border-border bg-surface">
          <div className="mx-auto flex max-w-3xl flex-wrap gap-2 px-3 py-2">
            {timers.map((t) => (
              <span key={t.id} className={cx("animate-pop inline-flex items-center gap-2 rounded-full py-1 pl-3 pr-1 text-sm font-semibold tabular-nums",
                t.done ? "animate-pulse bg-danger text-surface" : "bg-frost-soft text-frost")}>
                <Timer className="size-4" />
                {t.done ? `${t.label} done` : clock((t.endsAt - now) / 1000)}
                <span className="font-normal opacity-75">step {t.step}</span>
                <button onClick={() => setTimers((ts) => ts.filter((x) => x.id !== t.id))} className="rounded-full p-1.5 hover:bg-black/10"
                  aria-label={t.done ? "Dismiss timer" : "Cancel timer"}><X className="size-4" /></button>
              </span>
            ))}
          </div>
        </div>
      )}

      <main className="min-h-0 flex-1 overflow-y-auto">
        <div key={current} className={cx("mx-auto max-w-3xl px-5 py-6", slide)}>
          {current === 0 && (
            <>
              <h1 className="text-3xl font-bold">Get ready</h1>
              <p className="mt-1 text-muted">Gather everything first. Tap to tick things off.</p>
              <div className="mt-4 flex items-center gap-2">
                <span className="text-sm text-muted">Servings</span>
                <Button variant="secondary" className="px-3" onClick={() => setServings(Math.max(1, serves - 1))} aria-label="Fewer servings"><Minus className="size-4" /></Button>
                <span className="w-8 text-center text-lg font-semibold tabular-nums">{serves}</span>
                <Button variant="secondary" className="px-3" onClick={() => setServings(serves + 1)} aria-label="More servings"><Plus className="size-4" /></Button>
              </div>
              <IngredientChecklist ingredients={r.ingredients} factor={factor} checked={checked} onToggle={toggle} large />
            </>
          )}

          {step && (
            <>
              {step.section && <p className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted">{step.section}</p>}
              <p className="text-2xl leading-snug sm:text-3xl">{step.text}</p>

              {findTimers(step.text).length > 0 && (
                <div className="mt-6 flex flex-wrap gap-2">
                  {findTimers(step.text).map((t) => (
                    <Button key={t.seconds} variant="secondary" className="min-h-12 text-base" onClick={() => startTimer(t, step.number)}>
                      <Timer className="size-5" />Start {t.label} timer
                    </Button>
                  ))}
                </div>
              )}

              {mentionedIngredients(step.text, r.ingredients).length > 0 && (
                <section className="mt-8">
                  <h2 className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">In this step</h2>
                  <ul className="flex flex-col gap-1.5 text-lg">
                    {mentionedIngredients(step.text, r.ingredients).map((i) => (
                      <li key={i.id}>{amountText(i, factor) && <b className="mr-2">{amountText(i, factor)}</b>}{i.name}</li>
                    ))}
                  </ul>
                </section>
              )}
            </>
          )}

          {current === last && (
            <div className="flex flex-col items-center gap-3 py-10 text-center">
              <ChefHat className="size-12 text-accent" />
              <h1 className="text-3xl font-bold">Enjoy!</h1>
              <p className="text-muted">Take what you used out of inventory so the fridge stays accurate.</p>
              <Button variant="primary" className="mt-2 min-h-12 w-full max-w-sm text-base" onClick={() => setCooking(true)}>
                <Check className="size-5" />Take ingredients out of inventory
              </Button>
              <Button variant="ghost" className="w-full max-w-sm" onClick={() => {
                for (const k of ["page", "checked", "servings", "timers"]) try { sessionStorage.removeItem(`kitchen:${id}:${k}`); } catch { /* fine */ }
                exit();
              }}>Back to recipe</Button>
            </div>
          )}

          {steps.length === 0 && current === 0 && (
            <p className="mt-6 rounded-xl bg-surface-2 p-3 text-sm text-muted">This recipe has no method steps. Add them from Edit.</p>
          )}
        </div>
      </main>

      <footer className="pb-safe border-t border-border bg-surface">
        <div className="mx-auto flex max-w-3xl gap-3 px-3 py-3">
          <Button className="min-h-14 flex-1 text-base" disabled={current === 0} onClick={() => go(current - 1)}>
            <ArrowLeft className="size-5" />Back
          </Button>
          <Button variant="primary" className="min-h-14 flex-[2] text-base" disabled={current === last} onClick={() => go(current + 1)}>
            {current === 0 ? "Start cooking" : current === steps.length ? "Finish" : "Next"}<ArrowRight className="size-5" />
          </Button>
        </div>
      </footer>

      <Sheet open={showIngredients} onClose={() => setShowIngredients(false)} title={`Ingredients · serves ${serves}`}>
        <IngredientChecklist ingredients={r.ingredients} factor={factor} checked={checked} onToggle={toggle} />
      </Sheet>
      <CookSheet recipeId={r.id} servings={serves} open={cooking} onClose={() => setCooking(false)} />
    </div>
  );
}

const amountText = (i: Ingredient, factor: number) =>
  i.quantity != null ? `${prettyAmount(i.quantity * factor)}${i.unit ? ` ${i.unit}` : ""}` : null;

function IngredientChecklist({ ingredients, factor, checked, onToggle, large }: {
  ingredients: Ingredient[]; factor: number; checked: number[]; onToggle: (id: number) => void; large?: boolean;
}) {
  return (
    <ul className="mt-4 divide-y divide-border">
      {ingredients.map((i) => {
        const done = checked.includes(i.id);
        return (
          <li key={i.id}>
            <button onClick={() => { haptic(); onToggle(i.id); }} role="checkbox" aria-checked={done}
              className={cx("press flex w-full items-start gap-3 py-3 text-left", large && "text-lg")}>
              <TickBox checked={done} className="mt-0.5" />
              <span className={cx("min-w-0 flex-1 transition-colors duration-300", done && "text-muted line-through")}>
                {amountText(i, factor) && <b className="mr-1.5">{amountText(i, factor)}</b>}{i.name}
                {i.note && <span className="text-muted">, {i.note}</span>}
                {i.optional && <span className="text-muted"> (optional)</span>}
              </span>
            </button>
          </li>
        );
      })}
    </ul>
  );
}
