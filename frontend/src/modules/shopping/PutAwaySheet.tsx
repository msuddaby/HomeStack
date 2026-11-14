import { useEffect, useState } from "react";
import { PackageCheck } from "lucide-react";
import { todayISO } from "@/core/dates";
import { Button, cx, ErrorText, Input, Sheet } from "@/core/ui";
import { useCategories, useLocations, type Category, type Location } from "@/modules/inventory/api";
import { suggestExpiry } from "@/modules/inventory/logic";
import { usePutAway, type ShoppingItem } from "./api";

interface Line { add: boolean; locationId: number | null; quantity: string; expiresAt: string; expiryTouched: boolean }

function defaultLocation(cat: Category | undefined, locations: Location[]) {
  return (locations.find((l) => l.kind === (cat?.defaultLocation ?? "pantry")) ?? locations[0])?.id ?? null;
}

/** Review bought items, pick where each goes, and add them all to inventory in one go. */
export function PutAwaySheet({ open, items, onClose }: { open: boolean; items: ShoppingItem[]; onClose: () => void }) {
  const categories = useCategories().data ?? [];
  const locations = useLocations().data ?? [];
  const putAway = usePutAway();
  const [lines, setLines] = useState<Record<number, Line>>({});
  const today = todayISO();

  useEffect(() => {
    if (!open) return;
    putAway.reset();
    setLines(Object.fromEntries(items.map((i) => {
      const cat = categories.find((c) => c.id === i.category);
      // Non-food "other" items that aren't linked to a product default to just clearing.
      return [i.id, { add: !!i.productId || i.category !== "other", locationId: defaultLocation(cat, locations), quantity: i.quantity?.toString() ?? "1", expiresAt: "", expiryTouched: false }];
    })));
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const update = (id: number, patch: Partial<Line>) => setLines((ls) => ({ ...ls, [id]: { ...ls[id]!, ...patch } }));
  const adding = items.filter((i) => lines[i.id]?.add).length;

  const submit = async () => {
    await putAway.mutateAsync(items.map((i) => {
      const l = lines[i.id]!;
      return {
        shoppingItemId: i.id, addToInventory: l.add, locationId: l.add ? l.locationId : null,
        quantity: l.add ? Number(l.quantity) || 1 : null,
        expiresAt: l.add && l.expiryTouched && l.expiresAt ? l.expiresAt : null,
        noExpiry: l.add && l.expiryTouched && !l.expiresAt,
      };
    }));
    onClose();
  };

  return (
    <Sheet open={open} onClose={onClose} title="Put away"
      footer={<Button variant="primary" className="w-full" busy={putAway.isPending} onClick={submit} disabled={!items.length}>
        <PackageCheck className="size-5" />{adding ? `Add ${adding} to inventory` : "Clear from list"}{adding && adding < items.length ? `, clear ${items.length - adding}` : ""}
      </Button>}>
      <p className="mb-3 text-sm text-muted">Expiry dates are suggested from each item's category. Change any that the package says differently.</p>
      <ul className="flex flex-col gap-3">
        {items.map((i) => {
          const l = lines[i.id];
          if (!l) return null;
          const cat = categories.find((c) => c.id === i.category);
          const loc = locations.find((x) => x.id === l.locationId);
          const suggested = loc ? suggestExpiry(cat, loc.kind, today, loc.kind === "freezer" ? today : null, i.product ?? undefined) : null;
          return (
            <li key={i.id} className={cx("rounded-2xl border border-border p-3", !l.add && "opacity-60")}>
              <label className="flex items-center gap-2">
                <input type="checkbox" checked={l.add} onChange={(e) => update(i.id, { add: e.target.checked })} className="size-5 accent-[var(--color-accent)]" />
                <span className="flex-1 truncate font-medium">{cat?.emoji} {i.name}</span>
                {!l.add && <span className="text-xs text-muted">just clear</span>}
              </label>
              {l.add && (
                <div className="mt-2 flex flex-col gap-2">
                  <div className="flex flex-wrap gap-1.5">
                    {locations.map((x) => (
                      <button key={x.id} type="button" onClick={() => update(i.id, { locationId: x.id, expiryTouched: false })}
                        className={cx("min-h-9 rounded-lg border px-2.5 text-sm", l.locationId === x.id ? "border-accent bg-accent-soft font-medium text-accent" : "border-border")}>
                        {x.name}
                      </button>
                    ))}
                  </div>
                  <div className="grid grid-cols-[5rem_1fr] gap-2">
                    <Input inputMode="decimal" aria-label="Quantity" value={l.quantity} onChange={(e) => update(i.id, { quantity: e.target.value })} />
                    <Input type="date" aria-label="Expiry" value={l.expiryTouched ? l.expiresAt : suggested ?? ""}
                      onChange={(e) => update(i.id, { expiresAt: e.target.value, expiryTouched: true })} />
                  </div>
                </div>
              )}
            </li>
          );
        })}
      </ul>
      <div className="mt-3"><ErrorText error={putAway.error} /></div>
    </Sheet>
  );
}
