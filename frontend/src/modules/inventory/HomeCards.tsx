import { useMemo, useRef, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { AlertTriangle, Archive, ChevronRight, Clock, Flame, Plus, Snowflake } from "lucide-react";
import { duration } from "@/core/dates";
import type { HomeCard, RestockSuggestion } from "@/core/modules";
import { MotionItem, useExitingList, usePresence } from "@/core/motion";
import { Button, Card, Chip } from "@/core/ui";
import { useCategories, useDashboard, useMove, useUndoToast, useWasteStats, type Item, type LocationKind } from "./api";
import { ItemRow } from "./ItemRow";
import { ItemSheet } from "./ItemSheet";
import { formatMoney } from "./logic";

const KIND_ICON: Record<LocationKind, string> = { pantry: "🥫", fridge: "🧊", freezer: "❄️", other: "📦" };

function AddGroceries() {
  return (
    <Link to="/inventory?add=1" className="block">
      <Button variant="primary" className="w-full"><Plus className="size-4" />Add groceries</Button>
    </Link>
  );
}

/** Rows of items that open the item sheet when tapped. */
function useItemRows() {
  const categories = useCategories().data ?? [];
  const catById = useMemo(() => new Map(categories.map((c) => [c.id, c])), [categories]);
  const [selected, setSelected] = useState<Item | null>(null);
  const row = (i: Item, action?: ReactNode, motion?: { leaving: boolean; entering: boolean }) => (
    <ItemRow key={i.id} item={i} category={catById.get(i.product.category)} showLocation onClick={() => setSelected(i)} action={action} {...motion} />
  );
  return { row, sheet: <ItemSheet item={selected} onClose={() => setSelected(null)} /> };
}

function Expired() {
  const d = useDashboard().data;
  const { row, sheet } = useItemRows();
  return (
    <>
      <ItemSection items={d?.expired ?? []} title={<Title icon={<AlertTriangle className="size-4 text-danger" />} text="Expired" count={d?.expired.length ?? 0} />}
        intro={<p className="-mt-1 mb-2 text-xs text-muted">Tap one to use it up or throw it out.</p>}
        render={(i, m) => row(i, undefined, m)} />
      {sheet}
    </>
  );
}

function FreezeSoon() {
  const d = useDashboard().data;
  const move = useMove();
  const undoToast = useUndoToast();
  const { row, sheet } = useItemRows();
  const freezer = d?.counts.find((c) => c.kind === "freezer");
  return (
    <>
      <ItemSection items={d?.freezeSoon ?? []} title={<Title icon={<Snowflake className="size-4 text-frost" />} text="Freeze it or eat it" count={d?.freezeSoon.length ?? 0} />}
        intro={<p className="-mt-1 mb-2 text-xs text-muted">These expire in the next couple of days but keep well frozen.</p>}
        render={(i, m) => row(i, freezer && (
          <Button className="shrink-0 px-3" busy={move.isPending && move.variables?.id === i.id}
            onClick={async () => undoToast(await move.mutateAsync({ id: i.id, locationId: freezer.locationId }), `Froze ${i.product.name}`)}>
            <Snowflake className="size-4" />Freeze
          </Button>
        ), m)} />
      {sheet}
    </>
  );
}

function UseSoon() {
  const d = useDashboard().data;
  const { row, sheet } = useItemRows();
  return (
    <>
      <ItemSection items={d?.expiringSoon ?? []} title={<Title icon={<Clock className="size-4 text-warn" />} text="Use soon" count={d?.expiringSoon.length ?? 0} />}
        render={(i, m) => row(i, undefined, m)} />
      {sheet}
    </>
  );
}

function LongInFreezer() {
  const d = useDashboard().data;
  const { row, sheet } = useItemRows();
  return (
    <>
      <ItemSection items={(d?.longInFreezer ?? []).map((x) => ({ ...x.item, frozen: x }))}
        title={<Title icon={<Archive className="size-4 text-frost" />} text="Been in the freezer a while" count={d?.longInFreezer.length ?? 0} />}
        render={(i, m) => row(i, (
          <Chip tone={i.frozen.daysFrozen >= i.frozen.freezerDays ? "warn" : "frost"} className="shrink-0">{duration(i.frozen.daysFrozen)} / {duration(i.frozen.freezerDays)}</Chip>
        ), m)} />
      {sheet}
    </>
  );
}

/** How many things are in each place; tap one to see it. */
function Locations() {
  const d = useDashboard().data;
  if (!d) return null;
  return (
    <div className="grid grid-cols-3 gap-2">
      {d.counts.map((c) => (
        <Link key={c.locationId} to={`/inventory?loc=${c.locationId}`} className="rounded-2xl border border-border bg-surface p-3 hover:bg-surface-2">
          <span className="text-lg">{KIND_ICON[c.kind]}</span>
          <p className="mt-1 text-2xl font-bold tabular-nums">{c.items}</p>
          <p className="truncate text-xs text-muted">{c.name}</p>
        </Link>
      ))}
    </div>
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

/** Expired things are worth buying again (one suggestion per product). */
function useExpiredSuggestions(): RestockSuggestion[] {
  const expired = useDashboard().data?.expired ?? [];
  const seen = new Set<number>();
  return expired.filter((i) => !seen.has(i.product.id) && seen.add(i.product.id))
    .map((i) => ({ key: `expired-${i.product.id}`, name: i.product.name, productId: i.product.id, reason: "Expired" }));
}

export const inventoryHomeCards: HomeCard[] = [
  { id: "add-groceries", section: "actions", order: 10, Component: AddGroceries },
  { id: "expired", section: "attention", order: 20, Component: Expired },
  { id: "freeze-soon", section: "attention", order: 21, Component: FreezeSoon },
  { id: "use-soon", section: "today", order: 30, Component: UseSoon },
  { id: "locations", section: "glance", order: 20, Component: Locations },
  { id: "long-in-freezer", section: "glance", order: 30, Component: LongInFreezer },
  { id: "waste", section: "glance", order: 40, Component: WasteLine },
];
export { useExpiredSuggestions };

/**
 * A Home card of items that animates as things are dealt with: rows fold away (frozen, used up), and the
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
