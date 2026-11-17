import { useDeferredValue, useState } from "react";
import { Link } from "react-router";
import { Search } from "lucide-react";
import { Chip, Empty, Input, Spinner } from "@/core/ui";
import { useExpenseCategories, useItems } from "./api";
import { money, rhythmText } from "./format";

/** Everything bought on a scanned receipt, with how often and where. */
export function ItemsPage() {
  const [q, setQ] = useState("");
  const items = useItems(useDeferredValue(q));
  const emoji = new Map((useExpenseCategories().data ?? []).map((c) => [c.id, c.emoji]));

  return (
    <>
      <div className="relative mb-3">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted" />
        <Input className="pl-9" placeholder="Search items" value={q} onChange={(e) => setQ(e.target.value)} type="search" />
      </div>
      {!items.data ? <Spinner /> : !items.data.length ? (
        <Empty title={q ? "No matches" : "No items yet"}>Items appear here once you scan receipts.</Empty>
      ) : (
        <ul className="divide-y divide-border">
          {items.data.map((i) => (
            <li key={i.id}>
              <Link to={`/expenses/items/${i.id}`} className="flex items-center gap-3 py-2.5 hover:bg-surface-2 sm:px-2">
                <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-surface-2 text-lg" aria-hidden>{emoji.get(i.category) ?? "📦"}</span>
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium">{i.name}</p>
                  {i.rhythm && <p className="truncate text-xs text-muted">{rhythmText(i.rhythm)}</p>}
                </div>
                <div className="flex shrink-0 flex-col items-end gap-0.5">
                  {i.lastUnitPrice != null && <span className="text-sm tabular-nums">{money(i.lastUnitPrice)}</span>}
                  {i.rhythm?.due ? <Chip tone="warn">Due</Chip> : i.lastStore && <span className="text-xs text-muted">{i.lastStore}</span>}
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
