import { useEffect, useState } from "react";
import { ChefHat, Minus, Plus } from "lucide-react";
import { shortDate } from "@/core/dates";
import { Button, cx, ErrorText, Input, Select, Sheet, Spinner } from "@/core/ui";
import { formatQty } from "@/modules/inventory/logic";
import { fetchCookPlan, useCook, type CookPlan } from "./api";
import { prettyAmount } from "./format";

type Choice = { itemId: number | null; mode: "skip" | "some" | "all"; amount: string };

/** Confirm what cooking used, and take it out of inventory. */
/** `servings` omitted = use the recipe's own servings. */
export function CookSheet({ recipeId, servings: initialServings, planEntryId, open, onClose }: {
  recipeId: number; servings?: number; planEntryId?: number | null; open: boolean; onClose: () => void;
}) {
  const [servings, setServings] = useState<number | undefined>(initialServings);
  const [plan, setPlan] = useState<CookPlan | null>(null);
  const [choices, setChoices] = useState<Record<number, Choice>>({});
  const [loadError, setLoadError] = useState<unknown>(null);
  const cook = useCook();

  useEffect(() => { if (open) { setServings(initialServings); cook.reset(); } }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setLoadError(null);
    fetchCookPlan(recipeId, servings).then((p) => {
      if (cancelled) return;
      setPlan(p);
      setChoices(Object.fromEntries(p.lines.map((l) => [l.ingredientId, {
        itemId: l.suggestedItemId,
        mode: l.suggestedQuantity != null ? "some" : "skip",
        amount: l.suggestedQuantity != null ? String(l.suggestedQuantity) : "",
      } satisfies Choice])));
    }, (e) => !cancelled && setLoadError(e));
    return () => { cancelled = true; };
  }, [open, recipeId, servings]);

  const set = (id: number, patch: Partial<Choice>) => setChoices((c) => ({ ...c, [id]: { ...c[id]!, ...patch } }));
  const uses = Object.values(choices).filter((c) => c.itemId != null && c.mode !== "skip" && (c.mode === "all" || Number(c.amount) > 0))
    .map((c) => ({ itemId: c.itemId!, quantity: c.mode === "all" ? null : Number(c.amount) }));

  return (
    <Sheet open={open} onClose={onClose} title={plan ? `Cook ${plan.title}` : "Cook"}
      footer={<Button variant="primary" className="w-full" busy={cook.isPending} disabled={!plan}
        onClick={async () => { await cook.mutateAsync({ recipeId, planEntryId, uses }); onClose(); }}>
        <ChefHat className="size-5" />{uses.length ? `Cooked: update ${uses.length} ${uses.length === 1 ? "item" : "items"}` : "Mark as cooked"}
      </Button>}>
      <div className="mb-4 flex items-center justify-between rounded-2xl bg-surface-2 p-3">
        <span className="text-sm font-medium">Servings</span>
        <div className="flex items-center gap-2">
          <Button className="px-3" disabled={!plan} onClick={() => setServings(Math.max(1, (plan?.servings ?? 2) - 1))} aria-label="Fewer servings"><Minus className="size-4" /></Button>
          <span className="w-8 text-center font-semibold tabular-nums">{plan?.servings ?? "–"}</span>
          <Button className="px-3" disabled={!plan} onClick={() => setServings((plan?.servings ?? 0) + 1)} aria-label="More servings"><Plus className="size-4" /></Button>
        </div>
      </div>
      <ErrorText error={loadError} />
      {!plan ? <Spinner /> : (
        <ul className="flex flex-col gap-3">
          {plan.lines.map((l) => {
            const c = choices[l.ingredientId];
            if (!c) return null;
            const item = l.candidates.find((x) => x.itemId === c.itemId);
            return (
              <li key={l.ingredientId} className={cx("rounded-2xl border border-border p-3", c.mode === "skip" && "opacity-70")}>
                <p className="font-medium">{l.text}</p>
                {l.needed != null && <p className="text-xs text-muted">Needs {prettyAmount(l.needed)}{l.neededUnit ? ` ${l.neededUnit}` : ""} for {plan.servings}</p>}
                {l.candidates.length === 0 ? (
                  <p className="mt-1 text-xs text-muted">{l.productName ? `No ${l.productName} in inventory` : "Not tracked in inventory"}</p>
                ) : (
                  <div className="mt-2 flex flex-col gap-2">
                    {l.candidates.length > 1 && (
                      <Select value={c.itemId ?? ""} onChange={(e) => set(l.ingredientId, { itemId: Number(e.target.value) })}>
                        {l.candidates.map((x) => (
                          <option key={x.itemId} value={x.itemId}>{formatQty(x.quantity, x.unit) || "1"} · {x.location}{x.expiresAt ? ` · ${shortDate(x.expiresAt)}` : ""}</option>
                        ))}
                      </Select>
                    )}
                    <div className="flex items-center gap-1.5">
                      {(["skip", "some", "all"] as const).map((m) => (
                        <button key={m} type="button" onClick={() => set(l.ingredientId, { mode: m })}
                          className={cx("min-h-9 rounded-lg border px-3 text-sm", c.mode === m ? "border-accent bg-accent-soft font-medium text-accent" : "border-border")}>
                          {m === "skip" ? "Didn't use" : m === "some" ? "Used" : "Used it all"}
                        </button>
                      ))}
                      {c.mode === "some" && (
                        <div className="flex min-w-0 flex-1 items-center gap-1">
                          <Input inputMode="decimal" className="min-h-9 min-w-0" value={c.amount} onChange={(e) => set(l.ingredientId, { amount: e.target.value })} aria-label="Amount used" />
                          <span className="shrink-0 text-xs text-muted">{item?.unit ?? "pc"}</span>
                        </div>
                      )}
                    </div>
                    {item && <p className="text-xs text-muted">Have {formatQty(item.quantity, item.unit) || "1"} in {item.location}</p>}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}
      <div className="mt-3"><ErrorText error={cook.error} /></div>
    </Sheet>
  );
}
