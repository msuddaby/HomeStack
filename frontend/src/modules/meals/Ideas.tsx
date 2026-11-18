import { useState } from "react";
import { Link } from "react-router";
import { BookmarkPlus, CalendarPlus, Check, ChevronDown, Clock, ShoppingCart, Sparkles } from "lucide-react";
import { useAiSettings } from "@/core/ai";
import { errorMessage } from "@/core/api";
import { addDays, parseISO, todayISO } from "@/core/dates";
import { Button, Check as CheckBox, Chip, cx, Input, Select, Sheet, Spinner } from "@/core/ui";
import { fetchIdeas, useAcceptIdea, usePlanToList, type Idea } from "./api";
import { minutes } from "./format";

const WISHES = ["Quick", "Vegetarian", "Kid-friendly", "Comfort food", "Light"];

/** Preference chips plus free text, combined into one wish string. */
function WishPicker({ chips, setChips, text, setText }: { chips: string[]; setChips: (c: string[]) => void; text: string; setText: (t: string) => void }) {
  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap gap-1.5">
        {WISHES.map((w) => {
          const on = chips.includes(w);
          return (
            <button key={w} type="button" onClick={() => setChips(on ? chips.filter((c) => c !== w) : [...chips, w])}
              className={cx("rounded-full border px-2.5 py-1 text-xs", on ? "border-accent bg-accent-soft text-accent" : "border-border text-muted")}>{w}</button>
          );
        })}
      </div>
      <Input value={text} onChange={(e) => setText(e.target.value)} maxLength={200} placeholder="Anything else? e.g. no fish, use the slow cooker" />
    </div>
  );
}
const wishOf = (chips: string[], text: string) => [...chips, text.trim()].filter(Boolean).join(", ") || null;

const dayLabel = (iso: string) => iso === todayISO() ? "Tonight" : iso === addDays(todayISO(), 1) ? "Tomorrow"
  : parseISO(iso).toLocaleDateString(undefined, { weekday: "long" });

function AiNotSetUp() {
  return (
    <p className="text-sm text-muted">Meal ideas use an AI service. <Link to="/settings" className="font-medium text-accent underline">Set one up in Settings</Link>.</p>
  );
}

function IdeaFacts({ idea }: { idea: Idea }) {
  return (
    <div className="mt-1.5 flex flex-wrap items-center gap-1.5 text-xs">
      {idea.usesExpiring.map((e) => <Chip key={e.name} tone="warn">uses {e.name}</Chip>)}
      {idea.missing.length === 0 ? <Chip tone="accent">have everything</Chip>
        : <span className="text-muted">need {idea.missing.join(", ")}</span>}
      {minutes(idea.totalMinutes) && <span className="flex items-center gap-1 text-muted"><Clock className="size-3" />{minutes(idea.totalMinutes)}</span>}
    </div>
  );
}

function DraftDetails({ idea }: { idea: Idea }) {
  if (!idea.draft) return null;
  return (
    <details className="mt-2 text-sm">
      <summary className="flex cursor-pointer items-center gap-1 text-muted"><ChevronDown className="size-4" />Ingredients &amp; steps</summary>
      <ul className="mt-2 list-disc pl-5">{idea.draft.ingredients.map((l, i) => <li key={i}>{l}</li>)}</ul>
      <ol className="mt-2 list-decimal space-y-1 pl-5">{idea.draft.steps.map((l, i) => <li key={i}>{l}</li>)}</ol>
    </details>
  );
}

/** "Use it up": a few dinner ideas built around what's expiring. Lives at the top of "What can I make?". */
export function IdeasPanel() {
  const ai = useAiSettings();
  const [chips, setChips] = useState<string[]>([]);
  const [text, setText] = useState("");
  const [ideas, setIdeas] = useState<Idea[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [open, setOpen] = useState(false);

  async function go() {
    setBusy(true); setError(null);
    try { setIdeas((await fetchIdeas({ count: 4, wish: wishOf(chips, text) })).ideas); }
    catch (e) { setError(e); }
    finally { setBusy(false); }
  }

  return (
    <section className="rounded-2xl border border-accent/40 bg-accent-soft/40 p-3">
      <div className="flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 text-sm font-semibold"><Sparkles className="size-4 text-accent" />Use it up</h2>
        {ai.data?.configured && !busy && (
          <Button variant={ideas ? "ghost" : "primary"} className="min-h-9" onClick={go}>{ideas ? "More ideas" : "Get ideas"}</Button>
        )}
      </div>
      {!ai.data ? null : !ai.data.configured ? <div className="mt-2"><AiNotSetUp /></div> : (
        <>
          {!ideas && !busy && <p className="mt-1 text-sm text-muted">Dinner ideas from what you have, starting with what's about to go off.</p>}
          {!busy && (
            <button type="button" onClick={() => setOpen((o) => !o)} className="mt-2 flex items-center gap-1 text-xs text-muted">
              <ChevronDown className={cx("size-3.5 transition", open && "rotate-180")} />Preferences{wishOf(chips, text) ? `: ${wishOf(chips, text)}` : ""}
            </button>
          )}
          {open && !busy && <div className="mt-2"><WishPicker chips={chips} setChips={setChips} text={text} setText={setText} /></div>}
          {busy && <div className="flex items-center gap-2 py-4 text-sm text-muted"><Spinner />Thinking about dinner…</div>}
          {error != null && <p className="mt-2 text-sm text-danger">{errorMessage(error)}</p>}
          {ideas && !busy && (
            ideas.length === 0 ? <p className="mt-2 text-sm text-muted">No usable ideas came back. Try again or loosen the preferences.</p> : (
              <ul className="mt-2 flex flex-col gap-2">{ideas.map((i, n) => <IdeaCard key={i.title} idea={i} index={n} />)}</ul>
            )
          )}
        </>
      )}
    </section>
  );
}

/** `index` staggers the cards in as a batch of ideas arrives. */
function IdeaCard({ idea, index = 0 }: { idea: Idea; index?: number }) {
  const accept = useAcceptIdea();
  const [date, setDate] = useState(todayISO());
  const [done, setDone] = useState<null | { recipeId: number; planned: string | null }>(null);
  const days = Array.from({ length: 7 }, (_, i) => addDays(todayISO(), i));
  const recipeLink = done?.recipeId ?? idea.recipeId;

  async function keep(withDate: boolean) {
    const r = await accept.mutateAsync({ recipeId: done?.recipeId ?? idea.recipeId, draft: done || idea.recipeId ? null : idea.draft, date: withDate ? date : null });
    setDone({ recipeId: r.recipeId, planned: withDate ? date : done?.planned ?? null });
  }

  return (
    <li className="animate-rise rounded-xl border border-border bg-surface p-3" style={{ animationDelay: `${index * 60}ms` }}>
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="font-medium">{idea.title}</p>
          {idea.why && <p className="text-sm text-muted">{idea.why}</p>}
        </div>
        {idea.recipeId ? <Chip>saved recipe</Chip> : <Chip tone="accent">new</Chip>}
      </div>
      <IdeaFacts idea={idea} />
      <DraftDetails idea={idea} />

      {done?.planned ? (
        <p className="animate-rise mt-2 flex items-center gap-1.5 text-sm text-accent"><Check className="size-4" />On the plan for {dayLabel(done.planned).replace(/^(Tonight|Tomorrow)$/, (m) => m.toLowerCase())}
          {recipeLink && <Link to={`/meals/recipes/${recipeLink}`} className="ml-auto text-xs text-muted underline">Open recipe</Link>}</p>
      ) : (
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <Select value={date} onChange={(e) => setDate(e.target.value)} className="min-h-9 w-auto flex-1 text-sm" aria-label="Day">
            {days.map((d) => <option key={d} value={d}>{dayLabel(d)}</option>)}
          </Select>
          <Button variant="primary" className="min-h-9" busy={accept.isPending} onClick={() => keep(true)}><CalendarPlus className="size-4" />Plan</Button>
          {!idea.recipeId && !done && <Button className="min-h-9" busy={accept.isPending} onClick={() => keep(false)}><BookmarkPlus className="size-4" />Save</Button>}
          {recipeLink && <Link to={`/meals/recipes/${recipeLink}`}><Button variant="ghost" className="min-h-9">Open</Button></Link>}
        </div>
      )}
      {accept.error && <p className="mt-1 text-sm text-danger">{errorMessage(accept.error)}</p>}
    </li>
  );
}

/** "Plan my week": fills the free dinners in the next 7 days, reviewed before anything is saved. */
export function WeekIdeasSheet({ open, onClose, onAdded }: { open: boolean; onClose: () => void; onAdded?: (dates: string[]) => void }) {
  const ai = useAiSettings();
  const accept = useAcceptIdea();
  const toList = usePlanToList();
  const [chips, setChips] = useState<string[]>([]);
  const [text, setText] = useState("");
  const [ideas, setIdeas] = useState<Idea[] | null>(null);
  const [skip, setSkip] = useState<Set<number>>(new Set());
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState<number | null>(null);
  const [error, setError] = useState<unknown>(null);

  function reset() { setIdeas(null); setSkip(new Set()); setSaved(null); setError(null); toList.reset(); }
  function close() { reset(); onClose(); }

  async function go() {
    setBusy(true); setError(null); setSkip(new Set());
    try { setIdeas((await fetchIdeas({ week: true, wish: wishOf(chips, text) })).ideas); }
    catch (e) { setError(e); }
    finally { setBusy(false); }
  }

  async function addAll() {
    const chosen = (ideas ?? []).filter((_, i) => !skip.has(i));
    setBusy(true); setError(null);
    try {
      for (const i of chosen) await accept.mutateAsync({ recipeId: i.recipeId, draft: i.recipeId ? null : i.draft, date: i.date });
      setSaved(chosen.length);
      onAdded?.(chosen.flatMap((i) => (i.date ? [i.date] : [])));
    } catch (e) { setError(e); }
    finally { setBusy(false); }
  }

  const chosenCount = (ideas ?? []).length - skip.size;
  const needToBuy = [...new Set((ideas ?? []).filter((_, i) => !skip.has(i)).flatMap((i) => i.missing))];

  const footer = !ai.data?.configured ? undefined : saved != null ? (
    <div className="flex gap-2">
      {needToBuy.length > 0 && !toList.isSuccess && (
        <Button className="flex-1" busy={toList.isPending} onClick={() => toList.mutate({ from: todayISO(), to: addDays(todayISO(), 6) })}>
          <ShoppingCart className="size-4" />Add missing to list
        </Button>
      )}
      <Button className="flex-1" variant="primary" onClick={close}>Done</Button>
    </div>
  ) : ideas ? (
    <div className="flex gap-2">
      <Button className="shrink-0" disabled={busy} onClick={go}>Try again</Button>
      <Button className="flex-1" variant="primary" busy={busy} disabled={!chosenCount} onClick={addAll}>
        <CalendarPlus className="size-4" />Add {chosenCount} {chosenCount === 1 ? "dinner" : "dinners"}
      </Button>
    </div>
  ) : (
    <Button className="w-full" variant="primary" busy={busy} onClick={go}><Sparkles className="size-4" />{busy ? "Planning…" : "Plan my week"}</Button>
  );

  return (
    <Sheet open={open} onClose={close} title="Plan my week" footer={footer}>
      {!ai.data ? <Spinner /> : !ai.data.configured ? <AiNotSetUp /> : saved != null ? (
        <div className="flex flex-col gap-2 text-sm">
          <p className="animate-rise flex items-center gap-1.5 font-medium text-accent"><Check className="size-4" />Added {saved} {saved === 1 ? "dinner" : "dinners"} to the plan.</p>
          {toList.isSuccess ? <p className="text-muted">Added {toList.data.added} to the shopping list.</p>
            : needToBuy.length > 0 ? <p className="text-muted">To buy: {needToBuy.join(", ")}</p>
            : <p className="text-muted">You have everything for these.</p>}
          {error != null && <p className="text-danger">{errorMessage(error)}</p>}
        </div>
      ) : busy && !ideas ? (
        <div className="flex flex-col items-center gap-3 py-10 text-center text-sm text-muted"><Spinner />Planning dinners around what's expiring…</div>
      ) : ideas ? (
        <div className="flex flex-col gap-2">
          {ideas.length === 0 && <p className="text-sm text-muted">No usable plan came back. Try again.</p>}
          {ideas.map((idea, i) => (
            <div key={i} className={cx("animate-rise rounded-xl border border-border p-3 transition-opacity duration-200", skip.has(i) && "opacity-50")}
              style={{ animationDelay: `${i * 60}ms` }}>
              <div className="flex items-start gap-2">
                <CheckBox checked={!skip.has(i)} label={`Plan ${idea.title}`}
                  onChange={() => setSkip((s) => { const n = new Set(s); if (n.has(i)) n.delete(i); else n.add(i); return n; })} />
                <span className="min-w-0">
                  <span className="block text-xs font-semibold uppercase tracking-wide text-muted">{idea.date ? dayLabel(idea.date) : "Any day"}</span>
                  <span className="block font-medium">{idea.title}</span>
                  {idea.why && <span className="block text-sm text-muted">{idea.why}</span>}
                </span>
              </div>
              <IdeaFacts idea={idea} />
              <DraftDetails idea={idea} />
            </div>
          ))}
          {error != null && <p className="text-sm text-danger">{errorMessage(error)}</p>}
          <p className="text-xs text-muted">New recipes are saved with the tag "idea" so you can find or delete them later.</p>
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          <p className="text-sm text-muted">Fills each free dinner in the next 7 nights, using what expires first early in the week. Nothing is saved until you check it.</p>
          <WishPicker chips={chips} setChips={setChips} text={text} setText={setText} />
          {error != null && <p className="text-sm text-danger">{errorMessage(error)}</p>}
        </div>
      )}
    </Sheet>
  );
}
