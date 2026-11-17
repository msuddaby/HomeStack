import { useDeferredValue, useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import { ArrowLeft, Combine, Pencil } from "lucide-react";
import { shortDate } from "@/core/dates";
import { Button, Card, Chip, cx, ErrorText, Field, Input, Select, Sheet, Spinner } from "@/core/ui";
import { useExpenseCategories, useItem, useItems, useMergeItem, useUpdateItem, type TrackedItem } from "./api";
import { money, rhythmText } from "./format";

export function ItemPage() {
  const id = Number(useParams().id);
  const detail = useItem(id);
  const categories = useExpenseCategories().data ?? [];
  const [editing, setEditing] = useState(false);
  const [merging, setMerging] = useState(false);

  if (detail.error) return <ErrorText error={detail.error} />;
  const d = detail.data;
  if (!d) return <Spinner />;
  const cat = categories.find((c) => c.id === d.item.category);
  const cheapest = d.stores.length > 1 ? d.stores[0]?.store : null;

  return (
    <div className="flex flex-col gap-4">
      <Link to="/expenses/items" className="inline-flex items-center gap-1 text-sm text-muted"><ArrowLeft className="size-4" />Items</Link>
      <div>
        <h1 className="text-2xl font-bold">{d.item.name}</h1>
        <p className="text-sm text-muted">{cat ? `${cat.emoji} ${cat.label}` : d.item.category}</p>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button onClick={() => setEditing(true)}><Pencil className="size-4" />Edit</Button>
        <Button variant="ghost" onClick={() => setMerging(true)}><Combine className="size-4" />Same as another item…</Button>
      </div>

      <Card>
        <dl className="grid grid-cols-2 gap-3 text-sm">
          {d.item.rhythm && (
            <div className="col-span-2">
              <dt className="text-xs text-muted">How often</dt>
              <dd className="flex flex-wrap items-center gap-2">{rhythmText(d.item.rhythm)}{d.item.rhythm.due && <Chip tone="warn">Due about now</Chip>}</dd>
            </div>
          )}
          <div><dt className="text-xs text-muted">Bought</dt><dd>{d.purchases.length} {d.purchases.length === 1 ? "time" : "times"}</dd></div>
          <div><dt className="text-xs text-muted">Spent in total</dt><dd className="tabular-nums">{money(d.totalSpent)}</dd></div>
        </dl>
      </Card>

      <Card title="Price by store">
        <ul className="divide-y divide-border">
          {d.stores.map((s) => (
            <li key={s.store} className="flex items-center gap-3 py-2 text-sm">
              <div className="min-w-0 flex-1">
                <p className="flex items-center gap-2 font-medium">{s.store}{s.store === cheapest && <Chip tone="accent">Cheapest</Chip>}</p>
                <p className="text-xs text-muted">Last {shortDate(s.lastBought)} · {s.purchases} {s.purchases === 1 ? "time" : "times"}</p>
              </div>
              <div className="text-right">
                {s.lastUnitPrice != null && <p className="tabular-nums">{money(s.lastUnitPrice)}</p>}
                {s.lowestUnitPrice != null && s.lowestUnitPrice !== s.lastUnitPrice && <p className="text-xs text-muted">low {money(s.lowestUnitPrice)}</p>}
              </div>
            </li>
          ))}
        </ul>
        <p className="mt-2 text-xs text-muted">Price for one, after discounts.</p>
      </Card>

      <Card title="Purchases">
        <ul className="divide-y divide-border">
          {d.purchases.map((p, i) => (
            <li key={`${p.receiptId}-${i}`}>
              <Link to={`/expenses/receipts/${p.receiptId}`} className="flex items-center gap-3 py-2 text-sm hover:bg-surface-2">
                <span className="w-16 shrink-0 text-muted">{shortDate(p.date)}</span>
                <span className="min-w-0 flex-1 truncate">
                  {p.store ?? "No store"}{p.quantity !== 1 && <span className="text-muted"> ×{p.quantity}{p.unit ? ` ${p.unit}` : ""}</span>}
                  {p.discount ? <span className="text-accent"> −{money(p.discount)}</span> : null}
                </span>
                <span className="tabular-nums">{money(p.amount)}</span>
              </Link>
            </li>
          ))}
        </ul>
      </Card>

      {d.item.codes.length > 0 && <p className="text-xs text-muted">Store item numbers: <span className="font-mono">{d.item.codes.join(", ")}</span></p>}

      <EditSheet item={d.item} open={editing} onClose={() => setEditing(false)} />
      <MergeSheet item={d.item} open={merging} onClose={() => setMerging(false)} />
    </div>
  );
}

function EditSheet({ item, open, onClose }: { item: TrackedItem; open: boolean; onClose: () => void }) {
  const categories = useExpenseCategories().data ?? [];
  const update = useUpdateItem();
  const initial = () => ({ name: item.name, category: item.category, skipInventory: item.skipInventory });
  const [f, setF] = useState(initial);
  useEffect(() => { if (open) setF(initial()); }, [open, item]); // eslint-disable-line react-hooks/exhaustive-deps
  // Only food has an inventory choice to make.
  const food = item.productId != null || item.skipInventory || ["groceries", "snacks", "alcohol"].includes(f.category);
  return (
    <Sheet open={open} onClose={onClose} title="Edit item">
      <form className="flex flex-col gap-3" onSubmit={async (e) => {
        e.preventDefault();
        await update.mutateAsync({ id: item.id, name: f.name, category: f.category, skipInventory: food ? f.skipInventory : undefined });
        onClose();
      }}>
        <Field label="Name"><Input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} required /></Field>
        <Field label="Category">
          <Select value={f.category} onChange={(e) => setF({ ...f, category: e.target.value })}>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.emoji} {c.label}</option>)}
          </Select>
        </Field>
        {food && (
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" checked={!f.skipInventory} onChange={(e) => setF({ ...f, skipInventory: !e.target.checked })}
              className="mt-0.5 size-4 shrink-0 accent-[var(--color-accent)]" />
            <span>
              Put away in inventory when scanned
              <span className="block text-xs text-muted">Untick for things like snacks and pop you just want to track spending on.</span>
            </span>
          </label>
        )}
        {update.error && <ErrorText error={update.error} />}
        <Button variant="primary" busy={update.isPending}>Save</Button>
      </form>
    </Sheet>
  );
}

/** Pick another item that's really the same thing; its purchases and store numbers move onto this one. */
function MergeSheet({ item, open, onClose }: { item: TrackedItem; open: boolean; onClose: () => void }) {
  const [q, setQ] = useState("");
  const [picked, setPicked] = useState<TrackedItem | null>(null);
  const items = useItems(useDeferredValue(q));
  const merge = useMergeItem();
  useEffect(() => { if (open) { setQ(""); setPicked(null); merge.reset(); } }, [open]); // eslint-disable-line react-hooks/exhaustive-deps
  const others = (items.data ?? []).filter((i) => i.id !== item.id).slice(0, 30);

  return (
    <Sheet open={open} onClose={onClose} title="Same as another item">
      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted">
          Pick the item that's the same thing as <b className="text-text">{item.name}</b>, for example the same detergent bought at another store.
          Its purchases move here and its store numbers will link here from now on.
        </p>
        <Input placeholder="Search items" value={q} onChange={(e) => { setQ(e.target.value); setPicked(null); }} type="search" autoFocus />
        <ul className="max-h-72 divide-y divide-border overflow-y-auto">
          {others.map((o) => (
            <li key={o.id}>
              <button onClick={() => setPicked(o)} className={cx("flex w-full items-center gap-2 py-2 text-left text-sm", picked?.id === o.id && "text-accent")}>
                <span className="min-w-0 flex-1 truncate">{o.name}</span>
                <span className="shrink-0 text-xs text-muted">{o.lastStore}{o.lastUnitPrice != null ? ` · ${money(o.lastUnitPrice)}` : ""}</span>
              </button>
            </li>
          ))}
          {!others.length && <li className="py-2 text-sm text-muted">No other items{q ? " match" : ""}.</li>}
        </ul>
        {merge.error && <ErrorText error={merge.error} />}
        <Button variant="primary" disabled={!picked} busy={merge.isPending}
          onClick={async () => { await merge.mutateAsync({ id: picked!.id, intoItemId: item.id }); onClose(); }}>
          {picked ? `Combine "${picked.name}" into this` : "Pick an item"}
        </Button>
      </div>
    </Sheet>
  );
}
