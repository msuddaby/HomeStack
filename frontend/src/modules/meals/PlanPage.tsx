import { useMemo, useState } from "react";
import { Link } from "react-router";
import { CheckCircle2, ChefHat, ChevronLeft, ChevronRight, Pencil, Plus, ShoppingCart, Trash2, BookOpen } from "lucide-react";
import { addDays, parseISO, todayISO, toISO } from "@/core/dates";
import { Button, Chip, cx, ErrorText, Sheet } from "@/core/ui";
import { useDeletePlan, usePlan, usePlanToList, type PlanEntry } from "./api";
import { AddMealSheet } from "./AddMealSheet";
import { CookSheet } from "./CookSheet";
import { SLOT_LABEL } from "./format";

function weekStart(iso: string) {
  const d = parseISO(iso);
  const dow = (d.getDay() + 6) % 7; // Monday = 0
  return addDays(iso, -dow);
}

export function PlanPage() {
  const today = todayISO();
  const [start, setStart] = useState(() => weekStart(today));
  const end = addDays(start, 6);
  const plan = usePlan(start, end);
  const toList = usePlanToList();
  const [adding, setAdding] = useState<string | null>(null);
  const [selected, setSelected] = useState<PlanEntry | null>(null);

  const days = useMemo(() => Array.from({ length: 7 }, (_, i) => addDays(start, i)), [start]);
  const byDay = useMemo(() => {
    const m = new Map<string, PlanEntry[]>();
    for (const e of plan.data ?? []) m.set(e.date, [...(m.get(e.date) ?? []), e]);
    return m;
  }, [plan.data]);
  const missing = (plan.data ?? []).filter((e) => !e.cookedAt).reduce((s, e) => s + e.missingCount, 0);
  const label = `${parseISO(start).toLocaleDateString(undefined, { month: "short", day: "numeric" })} – ${parseISO(end).toLocaleDateString(undefined, { month: "short", day: "numeric" })}`;

  return (
    <>
      <div className="mb-3 flex items-center justify-between gap-2">
        <Button variant="ghost" className="px-2" onClick={() => setStart(addDays(start, -7))} aria-label="Previous week"><ChevronLeft className="size-5" /></Button>
        <button className="text-sm font-semibold" onClick={() => setStart(weekStart(today))}>{start === weekStart(today) ? "This week" : label}</button>
        <Button variant="ghost" className="px-2" onClick={() => setStart(addDays(start, 7))} aria-label="Next week"><ChevronRight className="size-5" /></Button>
      </div>

      {missing > 0 && (
        <div className="mb-3 flex items-center justify-between gap-2 rounded-2xl bg-warn-soft px-3 py-2 text-sm text-warn">
          <span>{toList.isSuccess ? `Added ${toList.data.added} to the shopping list` : `${missing} ingredient${missing === 1 ? "" : "s"} missing this week`}</span>
          {!toList.isSuccess && <Button className="min-h-9 shrink-0" busy={toList.isPending} onClick={() => toList.mutate({ from: start, to: end })}><ShoppingCart className="size-4" />Add to list</Button>}
        </div>
      )}
      <ErrorText error={toList.error ?? plan.error} />

      <div className="flex flex-col gap-2">
        {days.map((d) => {
          const entries = byDay.get(d) ?? [];
          const dt = parseISO(d);
          const isToday = d === today;
          return (
            <section key={d} className={cx("rounded-2xl border bg-surface p-3", isToday ? "border-accent" : "border-border", d < today && "opacity-75")}>
              <div className="flex items-center justify-between">
                <h2 className="text-sm font-semibold">
                  {dt.toLocaleDateString(undefined, { weekday: "long" })}
                  <span className="ml-1.5 font-normal text-muted">{dt.toLocaleDateString(undefined, { month: "short", day: "numeric" })}</span>
                  {isToday && <Chip tone="accent" className="ml-2">Today</Chip>}
                </h2>
                <button onClick={() => setAdding(d)} className="rounded-full p-1.5 text-muted hover:bg-surface-2 hover:text-text" aria-label={`Add meal on ${toISO(dt)}`}><Plus className="size-5" /></button>
              </div>
              {entries.length > 0 && (
                <ul className="mt-1 flex flex-col">
                  {entries.map((e) => (
                    <li key={e.id}>
                      <button onClick={() => setSelected(e)} className="flex w-full items-center gap-2 rounded-lg py-1.5 text-left hover:bg-surface-2">
                        <span className="w-16 shrink-0 text-xs text-muted">{SLOT_LABEL[e.slot]}</span>
                        <span className={cx("min-w-0 flex-1 truncate", e.cookedAt && "text-muted line-through")}>{e.title}</span>
                        {e.cookedAt ? <CheckCircle2 className="size-4 shrink-0 text-accent" />
                          : e.missingCount > 0 && <Chip tone="warn" className="shrink-0">need {e.missingCount}</Chip>}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          );
        })}
      </div>

      <AddMealSheet open={adding != null} date={adding ?? undefined} onClose={() => setAdding(null)} />
      <EntrySheet entry={selected} onClose={() => setSelected(null)} />
    </>
  );
}

function EntrySheet({ entry, onClose }: { entry: PlanEntry | null; onClose: () => void }) {
  const del = useDeletePlan();
  const [cooking, setCooking] = useState(false);
  const [editing, setEditing] = useState(false);
  if (!entry) return null;
  return (
    <>
      <Sheet open={!cooking && !editing} onClose={onClose} title={entry.title}>
        <div className="flex flex-col gap-2">
          <p className="text-sm text-muted">{SLOT_LABEL[entry.slot]} · {parseISO(entry.date).toLocaleDateString(undefined, { weekday: "long", month: "short", day: "numeric" })}
            {entry.cookedAt && " · cooked"}</p>
          {entry.recipeId && (
            <div className="grid grid-cols-2 gap-2">
              <Link to={`/meals/recipes/${entry.recipeId}`} className="contents"><Button><BookOpen className="size-4" />Open recipe</Button></Link>
              <Button variant="primary" onClick={() => setCooking(true)} disabled={!!entry.cookedAt}><ChefHat className="size-4" />Cook</Button>
            </div>
          )}
          <div className="grid grid-cols-2 gap-2">
            <Button variant="ghost" onClick={() => setEditing(true)}><Pencil className="size-4" />Move / change</Button>
            <Button variant="danger" busy={del.isPending} onClick={async () => { await del.mutateAsync(entry.id); onClose(); }}><Trash2 className="size-4" />Remove</Button>
          </div>
          <ErrorText error={del.error} />
        </div>
      </Sheet>
      {entry.recipeId && (
        <CookSheet recipeId={entry.recipeId} servings={entry.servings ?? undefined} planEntryId={entry.id} open={cooking}
          onClose={() => { setCooking(false); onClose(); }} />
      )}
      <AddMealSheet open={editing} entry={entry} onClose={() => { setEditing(false); onClose(); }} />
    </>
  );
}
