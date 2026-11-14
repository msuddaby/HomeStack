import { useDeferredValue, useMemo, useState } from "react";
import { Check, Plus, ShoppingCart, Sparkles, PackageCheck } from "lucide-react";
import { PageHeader } from "@/core/layout";
import { Button, Chip, cx, Empty, ErrorText, Spinner } from "@/core/ui";
import { useCategories, useProductSearch } from "@/modules/inventory/api";
import { formatQty, guessCategory } from "@/modules/inventory/logic";
import { useAddToList, useCheck, useClearChecked, useShoppingList, type ShoppingItem } from "./api";
import { EditListItemSheet } from "./EditListItemSheet";
import { PutAwaySheet } from "./PutAwaySheet";
import { parseEntry, SECTIONS, sectionFor } from "./sections";

export function ShoppingPage() {
  const list = useShoppingList(true);
  const [editing, setEditing] = useState<ShoppingItem | null>(null);
  const [puttingAway, setPuttingAway] = useState(false);
  const clear = useClearChecked();

  const items = list.data ?? [];
  const todo = items.filter((i) => !i.checkedAt);
  const inCart = items.filter((i) => i.checkedAt).sort((a, b) => b.checkedAt!.localeCompare(a.checkedAt!));
  const groups = useMemo(() => SECTIONS
    .map((s) => ({ ...s, items: todo.filter((i) => sectionFor(i.category).id === s.id).sort((a, b) => a.name.localeCompare(b.name)) }))
    .filter((g) => g.items.length > 0), [todo]);

  return (
    <>
      <PageHeader title="Shopping" subtitle={todo.length ? `${todo.length} to get` : undefined} />
      <AddBar />

      {list.isPending ? <Spinner /> : list.error ? <ErrorText error={list.error} /> : items.length === 0 ? (
        <Empty icon={<ShoppingCart className="size-10" />} title="The list is empty">
          Add things above. Anything you've set a minimum for shows up here automatically when it runs low.
        </Empty>
      ) : (
        <div className={cx("flex flex-col gap-5", inCart.length > 0 && "pb-20")}>
          {groups.map((g) => (
            <section key={g.id}>
              <h2 className="mb-1 text-xs font-semibold uppercase tracking-wide text-muted">{g.emoji} {g.label}</h2>
              <ul className="divide-y divide-border">{g.items.map((i) => <Row key={i.id} item={i} onEdit={() => setEditing(i)} />)}</ul>
            </section>
          ))}
          {todo.length === 0 && <p className="flex items-center gap-2 text-sm text-muted"><Sparkles className="size-4 text-accent" />Got everything!</p>}

          {inCart.length > 0 && (
            <section>
              <div className="mb-1 flex items-center justify-between">
                <h2 className="text-xs font-semibold uppercase tracking-wide text-muted">In the cart · {inCart.length}</h2>
                <button className="text-xs text-muted hover:text-text" onClick={() => clear.mutate()} disabled={clear.isPending}>Clear without adding</button>
              </div>
              <ul className="divide-y divide-border">{inCart.map((i) => <Row key={i.id} item={i} onEdit={() => setEditing(i)} />)}</ul>
            </section>
          )}
        </div>
      )}

      {inCart.length > 0 && (
        <div className="fixed inset-x-0 bottom-[calc(4.25rem+env(safe-area-inset-bottom))] z-30 px-4 md:bottom-6 md:left-56">
          <div className="mx-auto max-w-3xl">
            <Button variant="primary" className="w-full shadow-lg" onClick={() => setPuttingAway(true)}>
              <PackageCheck className="size-5" />Put away {inCart.length} {inCart.length === 1 ? "item" : "items"}
            </Button>
          </div>
        </div>
      )}

      <EditListItemSheet item={editing} onClose={() => setEditing(null)} />
      <PutAwaySheet open={puttingAway} items={inCart} onClose={() => setPuttingAway(false)} />
    </>
  );
}

function Row({ item, onEdit }: { item: ShoppingItem; onEdit: () => void }) {
  const check = useCheck();
  const done = !!item.checkedAt;
  const qty = item.quantity != null ? formatQty(item.quantity, item.unit) : null;
  return (
    <li className="flex items-center gap-1">
      <button onClick={() => check.mutate({ id: item.id, checked: !done })} aria-label={done ? `Uncheck ${item.name}` : `Check ${item.name}`}
        className="flex size-12 shrink-0 items-center justify-center">
        <span className={cx("flex size-6 items-center justify-center rounded-full border-2 transition",
          done ? "border-accent bg-accent text-accent-text" : "border-border")}>
          {done && <Check className="size-4" strokeWidth={3} />}
        </span>
      </button>
      <button onClick={onEdit} className="min-w-0 flex-1 py-2 text-left">
        <p className={cx("truncate", done && "text-muted line-through")}>
          {qty && <span className="mr-1.5 font-semibold">{qty.replace(/^×/, "")}</span>}{item.name}
        </p>
        {(item.note || item.source === "lowStock" || (done && item.checkedBy)) && (
          <div className="mt-0.5 flex flex-wrap items-center gap-1 text-xs text-muted">
            {item.source === "lowStock" && !done && <Chip tone="accent">Running low</Chip>}
            {item.note && <span className="truncate">{item.note}</span>}
            {done && item.checkedBy && <span>· {item.checkedBy}</span>}
          </div>
        )}
      </button>
    </li>
  );
}

function AddBar() {
  const [text, setText] = useState("");
  const add = useAddToList();
  const categories = useCategories().data ?? [];
  const parsed = parseEntry(text);
  const q = useDeferredValue(parsed.name);
  const matches = useProductSearch(q, 5).data ?? [];
  const [focused, setFocused] = useState(false);
  const showMatches = focused && text.trim().length > 0 && matches.length > 0;

  const submit = async (productId?: number) => {
    if (!parsed.name && !productId) return;
    await add.mutateAsync(productId
      ? { productId, quantity: parsed.quantity, unit: parsed.unit }
      : { name: parsed.name, category: guessCategory(parsed.name), quantity: parsed.quantity, unit: parsed.unit });
    setText("");
  };

  return (
    <div className="relative mb-5">
      <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); submit(); }}>
        <input value={text} onChange={(e) => setText(e.target.value)} onFocus={() => setFocused(true)} onBlur={() => setTimeout(() => setFocused(false), 150)}
          placeholder="Add an item, e.g. 2 milk" autoComplete="off" enterKeyHint="done"
          className="min-h-12 w-full rounded-2xl border border-border bg-surface px-4 outline-none focus:border-accent focus:ring-2 focus:ring-accent/25" />
        <Button variant="primary" className="min-h-12 shrink-0 rounded-2xl px-4" busy={add.isPending} disabled={!parsed.name} aria-label="Add"><Plus className="size-5" /></Button>
      </form>
      {showMatches && (
        <ul className="absolute inset-x-0 top-full z-10 mt-1 overflow-hidden rounded-xl border border-border bg-surface shadow-lg">
          {matches.map((p) => (
            <li key={p.id}>
              <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => submit(p.id)}
                className="flex w-full items-center gap-2 px-4 py-2.5 text-left hover:bg-surface-2">
                <span>{categories.find((c) => c.id === p.category)?.emoji}</span>
                <span className="flex-1 truncate">{parsed.quantity != null && <b className="mr-1">{parsed.quantity}{parsed.unit ? ` ${parsed.unit}` : ""}</b>}{p.name}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {add.error && <div className="mt-2"><ErrorText error={add.error} /></div>}
    </div>
  );
}
