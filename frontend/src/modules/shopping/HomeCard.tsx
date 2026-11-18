import { Link } from "react-router";
import { ChevronRight, Plus, ShoppingCart } from "lucide-react";
import { useModules, type RestockSuggestion } from "@/core/modules";
import { Button, Card } from "@/core/ui";
import { useAddToList, useShoppingList } from "./api";

const MAX_SUGGESTIONS = 5;

/**
 * The shopping list, plus everything other modules think is worth buying (expired food, things usually bought
 * about now), each one tap from the list. Things running low are on the list already.
 */
export function ShoppingDashboardWidget() {
  const list = useShoppingList();
  const add = useAddToList();
  // Module order is fixed, so these hooks run in the same order every render.
  const all = useModules().flatMap((m) => m.useRestockSuggestions?.() ?? []);
  if (!list.data) return null;

  const todo = list.data.filter((i) => !i.checkedAt);
  const listedNames = new Set(todo.map((i) => i.name.toLowerCase()));
  const listedProducts = new Set(todo.map((i) => i.productId).filter((id) => id != null));
  const seen = new Set<string>();
  const suggestions = all.filter((s) => {
    const name = s.name.toLowerCase();
    if (listedNames.has(name) || (s.productId != null && listedProducts.has(s.productId)) || seen.has(name)) return false;
    seen.add(name);
    return true;
  }).slice(0, MAX_SUGGESTIONS);

  return (
    <Card>
      <Link to="/shopping" className="flex items-center gap-3">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-accent-soft text-accent"><ShoppingCart className="size-5" /></span>
        <div className="min-w-0 flex-1">
          <p className="font-semibold">{todo.length ? `${todo.length} on the shopping list` : "Shopping list is empty"}</p>
          {todo.length > 0 && <p className="truncate text-sm text-muted">{todo.slice(0, 5).map((i) => i.name).join(", ")}{todo.length > 5 ? "…" : ""}</p>}
        </div>
        <ChevronRight className="size-5 shrink-0 text-muted" />
      </Link>
      {suggestions.length > 0 && (
        <div className="mt-3 border-t border-border pt-2">
          <p className="mb-1 text-xs font-medium text-muted">Worth adding?</p>
          <ul className="flex flex-col">
            {suggestions.map((s) => <Suggestion key={s.key} s={s} busy={add.isPending && add.variables?.name === s.name}
              onAdd={() => add.mutate(s.productId != null ? { productId: s.productId, name: s.name } : { name: s.name })} />)}
          </ul>
        </div>
      )}
    </Card>
  );
}

function Suggestion({ s, busy, onAdd }: { s: RestockSuggestion; busy: boolean; onAdd: () => void }) {
  return (
    <li className="flex items-center gap-2 py-1">
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-medium">{s.name}</p>
        <p className="truncate text-xs text-muted">{s.reason}</p>
      </div>
      <Button variant="ghost" className="min-h-9 shrink-0 px-2 text-xs" busy={busy} onClick={onAdd} aria-label={`Add ${s.name} to the list`}>
        <Plus className="size-4" />List
      </Button>
    </li>
  );
}
