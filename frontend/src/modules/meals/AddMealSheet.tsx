import { useDeferredValue, useEffect, useState } from "react";
import { Search } from "lucide-react";
import { shortDate, todayISO } from "@/core/dates";
import { Button, cx, ErrorText, Field, Input, Sheet } from "@/core/ui";
import { SLOTS, useRecipes, useSavePlan, type MealSlot, type PlanEntry } from "./api";
import { SLOT_LABEL } from "./format";

/** Plan a meal: pick a recipe or type something ("Takeout"). Also edits an existing entry. */
export function AddMealSheet({ open, onClose, date, recipe, entry }: {
  open: boolean; onClose: () => void; date?: string; recipe?: { id: number; title: string }; entry?: PlanEntry | null;
}) {
  const [d, setD] = useState(date ?? todayISO());
  const [slot, setSlot] = useState<MealSlot>("dinner");
  const [recipeId, setRecipeId] = useState<number | null>(null);
  const [title, setTitle] = useState("");
  const [q, setQ] = useState("");
  const recipes = useRecipes(useDeferredValue(q)).data ?? [];
  const save = useSavePlan();

  useEffect(() => {
    if (!open) return;
    save.reset();
    setD(entry?.date ?? date ?? todayISO());
    setSlot(entry?.slot ?? "dinner");
    setRecipeId(entry?.recipeId ?? recipe?.id ?? null);
    setTitle(entry && !entry.recipeId ? entry.title : "");
    setQ("");
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const chosen = recipes.find((r) => r.id === recipeId)?.title ?? (recipe?.id === recipeId ? recipe?.title : entry?.recipeId === recipeId ? entry?.title : null);
  const canSave = recipeId != null || title.trim().length > 0;

  return (
    <Sheet open={open} onClose={onClose} title={entry ? "Edit meal" : recipe ? `Plan ${recipe.title}` : `Plan a meal · ${shortDate(d)}`}
      footer={<Button variant="primary" className="w-full" disabled={!canSave} busy={save.isPending} onClick={async () => {
        await save.mutateAsync({ id: entry?.id, date: d, slot, recipeId, title: recipeId ? null : title });
        onClose();
      }}>{entry ? "Save" : "Add to plan"}</Button>}>
      <div className="flex flex-col gap-4">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Day"><Input type="date" value={d} onChange={(e) => setD(e.target.value)} /></Field>
          <div>
            <span className="mb-1 block text-xs font-medium text-muted">Meal</span>
            <div className="grid grid-cols-2 gap-1">
              {SLOTS.map((s) => (
                <button key={s} type="button" onClick={() => setSlot(s)}
                  className={cx("min-h-9 rounded-lg border text-xs", slot === s ? "border-accent bg-accent-soft font-medium text-accent" : "border-border")}>{SLOT_LABEL[s]}</button>
              ))}
            </div>
          </div>
        </div>

        {!recipe && (
          <>
            {recipeId != null && chosen ? (
              <div className="flex items-center justify-between rounded-xl bg-accent-soft px-3 py-2 text-sm text-accent">
                <span className="font-medium">{chosen}</span>
                <button className="text-xs underline" onClick={() => setRecipeId(null)}>Change</button>
              </div>
            ) : (
              <>
                <Field label="Something without a recipe" hint="e.g. Takeout, Leftovers, Eating out">
                  <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Type a meal…" />
                </Field>
                <div>
                  <span className="mb-1 block text-xs font-medium text-muted">…or pick a recipe</span>
                  <div className="relative mb-2">
                    <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted" />
                    <Input className="pl-9" placeholder="Search recipes" type="search" value={q} onChange={(e) => setQ(e.target.value)} />
                  </div>
                  <ul className="max-h-64 divide-y divide-border overflow-y-auto">
                    {recipes.map((r) => (
                      <li key={r.id}>
                        <button type="button" onClick={() => { setRecipeId(r.id); setTitle(""); }} className="flex w-full items-center justify-between py-2.5 text-left hover:bg-surface-2">
                          <span className="truncate">{r.title}</span>
                          <span className={cx("shrink-0 text-xs", r.missingCount ? "text-muted" : "text-accent")}>{r.missingCount ? `missing ${r.missingCount}` : "have all"}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                </div>
              </>
            )}
          </>
        )}
        <ErrorText error={save.error} />
      </div>
    </Sheet>
  );
}
