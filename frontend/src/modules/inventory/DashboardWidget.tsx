import { useMemo, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { AlertTriangle, Clock, Plus, Snowflake, ShoppingBasket, Sparkles, Archive, ShoppingCart, Check } from "lucide-react";
import { useAddToList } from "@/modules/shopping/api";
import { duration } from "@/core/dates";
import { Button, Card, Chip, ErrorText, Spinner } from "@/core/ui";
import { useCategories, useDashboard, useMove, type Item, type LocationKind } from "./api";
import { ItemRow } from "./ItemRow";
import { ItemSheet } from "./ItemSheet";
import { formatQty } from "./logic";
import { ProductSheet } from "./ProductSheet";

const KIND_ICON: Record<LocationKind, string> = { pantry: "🥫", fridge: "🧊", freezer: "❄️", other: "📦" };

export function InventoryDashboardWidget() {
  const dash = useDashboard();
  const categories = useCategories().data ?? [];
  const catById = useMemo(() => new Map(categories.map((c) => [c.id, c])), [categories]);
  const move = useMove();
  const addToList = useAddToList();
  const [listed, setListed] = useState<Set<number>>(new Set());
  const [selected, setSelected] = useState<Item | null>(null);
  const [productId, setProductId] = useState<number | null>(null);

  if (dash.isPending) return <Spinner />;
  if (dash.error) return <ErrorText error={dash.error} />;
  const d = dash.data;
  const freezer = d.counts.find((c) => c.kind === "freezer");
  const allClear = !d.expired.length && !d.expiringSoon.length && !d.freezeSoon.length && !d.longInFreezer.length && !d.lowStock.length;
  const row = (i: Item, action?: ReactNode) => (
    <ItemRow key={i.id} item={i} category={catById.get(i.product.category)} showLocation onClick={() => setSelected(i)} action={action} />
  );

  return (
    <>
      <div className="grid grid-cols-3 gap-2">
        {d.counts.map((c) => (
          <Link key={c.locationId} to={`/inventory?loc=${c.locationId}`} className="rounded-2xl border border-border bg-surface p-3 hover:bg-surface-2">
            <span className="text-lg">{KIND_ICON[c.kind]}</span>
            <p className="mt-1 text-2xl font-bold tabular-nums">{c.items}</p>
            <p className="truncate text-xs text-muted">{c.name}</p>
          </Link>
        ))}
      </div>

      <Link to="/inventory?add=1"><Button variant="primary" className="w-full"><Plus className="size-4" />Add groceries</Button></Link>

      {allClear && (
        <Card><div className="flex items-center gap-3 text-sm"><Sparkles className="size-5 text-accent" />Nothing needs attention. Everything's fresh.</div></Card>
      )}

      {d.freezeSoon.length > 0 && (
        <Card title={<Title icon={<Snowflake className="size-4 text-frost" />} text="Freeze it or eat it" count={d.freezeSoon.length} />}>
          <p className="-mt-1 mb-2 text-xs text-muted">These expire in the next couple of days but keep well frozen.</p>
          <ul className="divide-y divide-border">
            {d.freezeSoon.map((i) => row(i, freezer && (
              <Button className="shrink-0 px-3" busy={move.isPending && move.variables?.id === i.id}
                onClick={() => move.mutate({ id: i.id, locationId: freezer.locationId })}><Snowflake className="size-4" />Freeze</Button>
            )))}
          </ul>
        </Card>
      )}

      {d.expired.length > 0 && (
        <Card title={<Title icon={<AlertTriangle className="size-4 text-danger" />} text="Expired" count={d.expired.length} />}>
          <ul className="divide-y divide-border">
            {d.expired.map((i) => row(i, (
              <Button className="shrink-0 px-3" aria-label={`Add ${i.product.name} to shopping list`} disabled={listed.has(i.product.id)}
                onClick={() => addToList.mutate({ productId: i.product.id }, { onSuccess: () => setListed((s) => new Set(s).add(i.product.id)) })}>
                {listed.has(i.product.id) ? <Check className="size-4" /> : <ShoppingCart className="size-4" />}
              </Button>
            )))}
          </ul>
        </Card>
      )}

      {d.expiringSoon.length > 0 && (
        <Card title={<Title icon={<Clock className="size-4 text-warn" />} text="Use soon" count={d.expiringSoon.length} />}>
          <ul className="divide-y divide-border">{d.expiringSoon.map((i) => row(i))}</ul>
        </Card>
      )}

      {d.longInFreezer.length > 0 && (
        <Card title={<Title icon={<Archive className="size-4 text-frost" />} text="Been in the freezer a while" count={d.longInFreezer.length} />}>
          <ul className="divide-y divide-border">
            {d.longInFreezer.map((x) => row(x.item, (
              <Chip tone={x.daysFrozen >= x.freezerDays ? "warn" : "frost"} className="shrink-0">{duration(x.daysFrozen)} / {duration(x.freezerDays)}</Chip>
            )))}
          </ul>
        </Card>
      )}

      {d.lowStock.length > 0 && (
        <Card title={<Title icon={<ShoppingBasket className="size-4 text-accent" />} text="Running low" count={d.lowStock.length} />}
          action={<Link to="/shopping" className="text-xs text-accent">On your shopping list →</Link>}>
          <ul className="divide-y divide-border text-sm">
            {d.lowStock.map((x) => (
              <li key={x.product.id}>
                <button onClick={() => setProductId(x.product.id)} className="flex w-full items-center justify-between py-2 text-left">
                  <span>{catById.get(x.product.category)?.emoji} {x.product.name}</span>
                  <span className="text-muted">{formatQty(x.quantity, x.product.defaultUnit) || "0"} of {x.product.minQuantity}</span>
                </button>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <ItemSheet item={selected} onClose={() => setSelected(null)} />
      <ProductSheet productId={productId} onClose={() => setProductId(null)} />
    </>
  );
}

function Title({ icon, text, count }: { icon: ReactNode; text: string; count: number }) {
  return <span className="flex items-center gap-2">{icon}{text}<span className="font-normal text-muted">{count}</span></span>;
}
