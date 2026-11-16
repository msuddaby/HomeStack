import { useDeferredValue, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { Flame, Plus, Search, Refrigerator, Tags } from "lucide-react";
import { PageHeader } from "@/core/layout";
import { Button, cx, Empty, Input, Spinner } from "@/core/ui";
import { useCategories, useDashboard, useItems, useLocations, type Item } from "./api";
import { AddItemSheet } from "./AddItemSheet";
import { ItemRow } from "./ItemRow";
import { ItemSheet } from "./ItemSheet";

export function InventoryPage() {
  const [params, setParams] = useSearchParams();
  const locationId = Number(params.get("loc")) || undefined;
  const adding = params.get("add") === "1";
  const [q, setQ] = useState("");
  const query = useDeferredValue(q);
  const [selectedId, setSelectedId] = useState<number | null>(null);

  const locations = useLocations().data ?? [];
  const counts = useDashboard().data?.counts;
  const categories = useCategories().data ?? [];
  const items = useItems({ locationId, q: query });
  const catById = useMemo(() => new Map(categories.map((c) => [c.id, c])), [categories]);
  const selected: Item | null = items.data?.find((i) => i.id === selectedId) ?? null;
  const total = counts?.reduce((s, c) => s + c.items, 0);

  const setParam = (k: string, v: string | null) => setParams((p) => { if (v == null) p.delete(k); else p.set(k, v); return p; }, { replace: true });

  return (
    <>
      <PageHeader title="Inventory" action={
        <div className="flex gap-2">
          <Link to="/inventory/waste"><Button variant="ghost" className="px-3" aria-label="Waste"><Flame className="size-4" /><span className="hidden sm:inline">Waste</span></Button></Link>
          <Link to="/inventory/products"><Button variant="ghost" className="px-3" aria-label="Products"><Tags className="size-4" /><span className="hidden sm:inline">Products</span></Button></Link>
          <Button variant="primary" className="hidden md:inline-flex" onClick={() => setParam("add", "1")}><Plus className="size-4" />Add</Button>
        </div>
      } />

      <div className="-mx-4 mb-3 flex gap-2 overflow-x-auto px-4 pb-1">
        <Tab active={!locationId} onClick={() => setParam("loc", null)} label="All" count={total} />
        {locations.map((l) => (
          <Tab key={l.id} active={locationId === l.id} onClick={() => setParam("loc", String(l.id))} label={l.name}
            count={counts?.find((c) => c.locationId === l.id)?.items} />
        ))}
      </div>

      <div className="relative mb-3">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted" />
        <Input className="pl-9" placeholder="Search items" value={q} onChange={(e) => setQ(e.target.value)} type="search" />
      </div>

      {items.isPending ? <Spinner /> : items.data?.length ? (
        <ul className="divide-y divide-border">
          {items.data.map((i) => (
            <ItemRow key={i.id} item={i} category={catById.get(i.product.category)} showLocation={!locationId} onClick={() => setSelectedId(i.id)} />
          ))}
        </ul>
      ) : (
        <Empty icon={<Refrigerator className="size-10" />} title={q ? "No matches" : "Nothing here yet"}>
          {!q && <Button variant="primary" className="mt-3" onClick={() => setParam("add", "1")}><Plus className="size-4" />Add groceries</Button>}
        </Empty>
      )}

      <button onClick={() => setParam("add", "1")} aria-label="Add item"
        className="fixed bottom-24 right-4 z-30 flex size-14 items-center justify-center rounded-full bg-accent text-accent-text shadow-lg md:hidden">
        <Plus className="size-7" />
      </button>

      <AddItemSheet open={adding} onClose={() => setParam("add", null)} defaultLocationId={locationId} />
      <ItemSheet item={selected} onClose={() => setSelectedId(null)} />
    </>
  );
}

function Tab({ active, onClick, label, count }: { active: boolean; onClick: () => void; label: string; count?: number }) {
  return (
    <button onClick={onClick} className={cx("flex min-h-10 shrink-0 items-center gap-1.5 rounded-full border px-4 text-sm font-medium",
      active ? "border-accent bg-accent text-accent-text" : "border-border bg-surface text-text")}>
      {label}{count != null && <span className={cx("text-xs", active ? "opacity-80" : "text-muted")}>{count}</span>}
    </button>
  );
}
