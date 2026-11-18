import { useMemo, useRef, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { AlertTriangle, Clock, Plus, Snowflake, ShoppingBasket, Sparkles, Archive, ShoppingCart, Check, Flame, ChevronRight } from "lucide-react";
import { useAddToList } from "@/modules/shopping/api";
import { duration } from "@/core/dates";
import { MotionItem, useExitingList, usePresence } from "@/core/motion";
import { Button, Card, Chip, ErrorText, Spinner } from "@/core/ui";
import { useCategories, useDashboard, useMove, useUndoToast, useWasteStats, type Item, type LocationKind } from "./api";
import { ItemRow } from "./ItemRow";
import { ItemSheet } from "./ItemSheet";
import { formatMoney, formatQty } from "./logic";
import { ProductSheet } from "./ProductSheet";

const KIND_ICON: Record<LocationKind, string> = { pantry: "🥫", fridge: "🧊", freezer: "❄️", other: "📦" };

export function InventoryDashboardWidget() {
  const dash = useDashboard();
  const categories = useCategories().data ?? [];
  const catById = useMemo(() => new Map(categories.map((c) => [c.id, c])), [categories]);
  const move = useMove();
  const undoToast = useUndoToast();
  const addToList = useAddToList();
  const [listed, setListed] = useState<Set<number>>(new Set());
  const [selected, setSelected] = useState<Item | null>(null);
  const [productId, setProductId] = useState<number | null>(null);

  if (dash.isPending) return <Spinner />;
  if (dash.error) return <ErrorText error={dash.error} />;
  const d = dash.data;
  const freezer = d.counts.find((c) => c.kind === "freezer");
  const allClear = !d.expired.length && !d.expiringSoon.length && !d.freezeSoon.length && !d.longInFreezer.length && !d.lowStock.length;
  const row = (i: Item, action?: ReactNode, motion?: { leaving: boolean; entering: boolean }) => (
    <ItemRow key={i.id} item={i} category={catById.get(i.product.category)} showLocation onClick={() => setSelected(i)} action={action} {...motion} />
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
        <Card className="animate-rise"><div className="flex items-center gap-3 text-sm"><Sparkles className="size-5 text-accent" />Nothing needs attention. Everything's fresh.</div></Card>
      )}

      <ItemSection items={d.freezeSoon} title={<Title icon={<Snowflake className="size-4 text-frost" />} text="Freeze it or eat it" count={d.freezeSoon.length} />}
        intro={<p className="-mt-1 mb-2 text-xs text-muted">These expire in the next couple of days but keep well frozen.</p>}
        render={(i, m) => row(i, freezer && (
          <Button className="shrink-0 px-3" busy={move.isPending && move.variables?.id === i.id}
            onClick={async () => undoToast(await move.mutateAsync({ id: i.id, locationId: freezer.locationId }), `Froze ${i.product.name}`)}>
            <Snowflake className="size-4" />Freeze
          </Button>
        ), m)} />

      <ItemSection items={d.expired} title={<Title icon={<AlertTriangle className="size-4 text-danger" />} text="Expired" count={d.expired.length} />}
        render={(i, m) => row(i, (
          <Button className="shrink-0 px-3" aria-label={`Add ${i.product.name} to shopping list`} disabled={listed.has(i.product.id)}
            onClick={() => addToList.mutate({ productId: i.product.id }, { onSuccess: () => setListed((s) => new Set(s).add(i.product.id)) })}>
            {listed.has(i.product.id) ? <Check className="size-4 animate-pop text-accent" /> : <ShoppingCart className="size-4" />}
          </Button>
        ), m)} />

      <ItemSection items={d.expiringSoon} title={<Title icon={<Clock className="size-4 text-warn" />} text="Use soon" count={d.expiringSoon.length} />}
        render={(i, m) => row(i, undefined, m)} />

      <ItemSection items={d.longInFreezer.map((x) => ({ ...x.item, frozen: x }))}
        title={<Title icon={<Archive className="size-4 text-frost" />} text="Been in the freezer a while" count={d.longInFreezer.length} />}
        render={(i, m) => row(i, (
          <Chip tone={i.frozen.daysFrozen >= i.frozen.freezerDays ? "warn" : "frost"} className="shrink-0">{duration(i.frozen.daysFrozen)} / {duration(i.frozen.freezerDays)}</Chip>
        ), m)} />

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

      <WasteLine />

      <ItemSheet item={selected} onClose={() => setSelected(null)} />
      <ProductSheet productId={productId} onClose={() => setProductId(null)} />
    </>
  );
}

/** "This month: 4 thrown out ($11.20), vs 6 last month". Hidden when nothing was thrown out either month. */
function WasteLine() {
  const stats = useWasteStats(2).data;
  if (!stats) return null;
  const [last, now] = stats.months;
  if (!now || !last || (!now.wasted && !last.wasted)) return null;
  return (
    <Link to="/inventory/waste" className="flex items-center gap-3 rounded-2xl border border-border bg-surface p-3 text-sm hover:bg-surface-2">
      <Flame className="size-5 shrink-0 text-danger" />
      <span className="min-w-0 flex-1">
        This month: <b>{now.wasted} thrown out</b>{now.wastedValue != null && ` (${formatMoney(now.wastedValue)})`}
        <span className="text-muted">, vs {last.wasted} last month</span>
      </span>
      <ChevronRight className="size-4 shrink-0 text-muted" />
    </Link>
  );
}

/**
 * A dashboard card of items that animates as things are dealt with: rows fold away (frozen, used up), and the
 * card itself folds away once it's empty.
 */
function ItemSection<T extends Item>({ items, title, intro, render }: {
  items: T[]; title: ReactNode; intro?: ReactNode; render: (item: T, motion: { leaving: boolean; entering: boolean }) => ReactNode;
}) {
  const rows = useExitingList(items, (i) => i.id);
  const { mounted, leaving } = usePresence(items.length > 0, 280);
  // Keep the title (with its count) as it was while the card folds away.
  const lastTitle = useRef(title);
  if (items.length) lastTitle.current = title;
  if (!mounted) return null;
  return (
    <MotionItem leaving={leaving}>
      <Card title={lastTitle.current}>
        {intro}
        <ul>{rows.map(({ item, leaving, entering }) => render(item, { leaving, entering }))}</ul>
      </Card>
    </MotionItem>
  );
}

function Title({ icon, text, count }: { icon: ReactNode; text: string; count: number }) {
  return <span className="flex items-center gap-2">{icon}{text}<span className="font-normal text-muted">{count}</span></span>;
}
