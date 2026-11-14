import { useEffect, useState } from "react";
import { Minus, Plus, Snowflake, Trash2, Utensils, Flame, Pencil, PackageOpen } from "lucide-react";
import { ApiError } from "@/core/api";
import { shortDate, todayISO } from "@/core/dates";
import { Button, Chip, ErrorText, Field, Input, Select, Sheet } from "@/core/ui";
import { useCategories, useConsume, useDeleteItem, useLocations, useMove, useUpdateItem, type Item } from "./api";
import { formatQty, itemStatus } from "./logic";

export function ItemSheet({ item, onClose }: { item: Item | null; onClose: () => void }) {
  const [editing, setEditing] = useState(false);
  useEffect(() => setEditing(false), [item?.id]);
  return (
    <Sheet open={!!item} onClose={onClose} title={item?.product.name ?? ""}>
      {item && (editing
        ? <EditForm item={item} onDone={() => setEditing(false)} />
        : <Actions item={item} onClose={onClose} onEdit={() => setEditing(true)} />)}
    </Sheet>
  );
}

function Actions({ item, onClose, onEdit }: { item: Item; onClose: () => void; onEdit: () => void }) {
  const categories = useCategories().data ?? [];
  const locations = useLocations().data ?? [];
  const consume = useConsume();
  const move = useMove();
  const update = useUpdateItem();
  const del = useDeleteItem();
  const [amount, setAmount] = useState(1);
  const cat = categories.find((c) => c.id === item.product.category);
  const status = itemStatus(item);
  const inFreezer = item.location.kind === "freezer";
  const freezable = (item.product.freezerDays ?? cat?.freezerDays) != null;
  const step = item.quantity >= 2 ? 1 : item.quantity / 2;
  const error = consume.error ?? move.error ?? update.error ?? del.error;

  const use = async (quantity: number | undefined, wasted = false) => {
    const remaining = await consume.mutateAsync({ id: item.id, quantity, wasted });
    if (!remaining) onClose();
  };

  return (
    <div className="flex flex-col gap-5">
      <div className="flex items-start gap-3">
        {item.product.imageUrl
          ? <img src={item.product.imageUrl} alt="" className="size-16 rounded-xl bg-white object-contain" />
          : <span className="flex size-16 items-center justify-center rounded-xl bg-surface-2 text-3xl">{cat?.emoji}</span>}
        <div className="min-w-0 flex-1 text-sm">
          <p className="text-muted">{[item.product.brand, cat?.label].filter(Boolean).join(" · ")}</p>
          <p className="mt-1 text-base font-semibold">{formatQty(item.quantity, item.unit) || "1"} · {item.location.name}</p>
          <div className="mt-1 flex flex-wrap gap-1">{status && <Chip tone={status.tone}>{status.label}</Chip>}</div>
        </div>
        <Button variant="ghost" onClick={onEdit} aria-label="Edit" className="px-3"><Pencil className="size-4" /></Button>
      </div>

      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 rounded-2xl bg-surface-2 p-3 text-sm">
        {item.expiresAt && <Detail label={inFreezer ? "Best before" : "Expires"} value={shortDate(item.expiresAt)} />}
        {item.frozenAt && <Detail label="Frozen" value={shortDate(item.frozenAt)} />}
        {item.purchasedAt && <Detail label="Bought" value={shortDate(item.purchasedAt)} />}
        {item.openedAt && <Detail label="Opened" value={shortDate(item.openedAt)} />}
        {item.notes && <div className="col-span-2"><dt className="text-xs text-muted">Notes</dt><dd>{item.notes}</dd></div>}
      </dl>

      <section className="flex flex-col gap-2">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-muted">Use it</h3>
        {item.quantity > step && (
          <div className="flex items-center gap-2">
            <Button className="px-3" onClick={() => setAmount((a) => Math.max(step, +(a - step).toFixed(3)))} aria-label="Less"><Minus className="size-4" /></Button>
            <span className="w-16 text-center font-semibold tabular-nums">{formatQty(amount, item.unit) || "1"}</span>
            <Button className="px-3" onClick={() => setAmount((a) => Math.min(item.quantity, +(a + step).toFixed(3)))} aria-label="More"><Plus className="size-4" /></Button>
            <Button className="flex-1" busy={consume.isPending} onClick={() => use(amount)}><Utensils className="size-4" />Use {formatQty(amount, item.unit) || "1"}</Button>
          </div>
        )}
        <div className="grid grid-cols-2 gap-2">
          <Button variant="primary" busy={consume.isPending} onClick={() => use(undefined)}><Utensils className="size-4" />Used it all</Button>
          <Button variant="danger" busy={consume.isPending} onClick={() => use(undefined, true)}><Flame className="size-4" />Threw it out</Button>
        </div>
        {!item.openedAt && !inFreezer && (
          <Button variant="ghost" busy={update.isPending} onClick={() => update.mutate({ ...toUpdate(item), openedAt: todayISO() })}>
            <PackageOpen className="size-4" />Mark opened today
          </Button>
        )}
      </section>

      <section className="flex flex-col gap-2">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-muted">Move</h3>
        <div className="flex flex-wrap gap-2">
          {locations.filter((l) => l.id !== item.location.id).map((l) => {
            const freezing = l.kind === "freezer" && !inFreezer;
            const thawing = inFreezer && l.kind !== "freezer";
            if (freezing && !freezable) return null;
            return (
              <Button key={l.id} variant={freezing ? "primary" : "secondary"} busy={move.isPending} onClick={() => move.mutate({ id: item.id, locationId: l.id })}>
                {freezing && <Snowflake className="size-4" />}
                {freezing ? `Freeze (${l.name})` : thawing ? `Thaw → ${l.name}` : l.name}
              </Button>
            );
          })}
        </div>
        {inFreezer && <p className="text-xs text-muted">Thawing sets a short use-by date for the fridge.</p>}
      </section>

      <ErrorText error={error} />
      <button className="flex items-center gap-1 self-center text-xs text-muted hover:text-danger" onClick={async () => { await del.mutateAsync(item.id); onClose(); }}>
        <Trash2 className="size-3" />Delete entry (added by mistake)
      </button>
    </div>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs text-muted">{label}</dt><dd className="font-medium">{value}</dd></div>;
}

const toUpdate = (i: Item) => ({
  id: i.id, locationId: i.location.id, quantity: i.quantity, unit: i.unit, purchasedAt: i.purchasedAt,
  expiresAt: i.expiresAt, openedAt: i.openedAt, frozenAt: i.frozenAt, notes: i.notes,
});

function EditForm({ item, onDone }: { item: Item; onDone: () => void }) {
  const locations = useLocations().data ?? [];
  const update = useUpdateItem();
  const [f, setF] = useState(() => ({ ...toUpdate(item), quantity: String(item.quantity) }));
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });
  const loc = locations.find((l) => l.id === Number(f.locationId));
  const err = update.error instanceof ApiError ? update.error : undefined;
  const nul = (s: string | null) => (s ? s : null);

  return (
    <form className="grid grid-cols-2 gap-3" onSubmit={async (e) => {
      e.preventDefault();
      await update.mutateAsync({
        ...f, locationId: Number(f.locationId), quantity: Number(f.quantity), unit: nul(f.unit), notes: nul(f.notes),
        purchasedAt: nul(f.purchasedAt), expiresAt: nul(f.expiresAt), openedAt: nul(f.openedAt), frozenAt: loc?.kind === "freezer" ? nul(f.frozenAt) : null,
      });
      onDone();
    }}>
      <Field label="Location" className="col-span-2">
        <Select value={f.locationId} onChange={set("locationId")}>{locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</Select>
      </Field>
      <Field label="Quantity" error={err?.field("quantity")}><Input inputMode="decimal" value={f.quantity} onChange={set("quantity")} /></Field>
      <Field label="Unit"><Input value={f.unit ?? ""} onChange={set("unit")} /></Field>
      <Field label={loc?.kind === "freezer" ? "Best before" : "Expires"}><Input type="date" value={f.expiresAt ?? ""} onChange={set("expiresAt")} /></Field>
      <Field label="Purchased"><Input type="date" value={f.purchasedAt ?? ""} onChange={set("purchasedAt")} /></Field>
      <Field label="Opened"><Input type="date" value={f.openedAt ?? ""} onChange={set("openedAt")} /></Field>
      {loc?.kind === "freezer" && <Field label="Date frozen"><Input type="date" value={f.frozenAt ?? ""} onChange={set("frozenAt")} /></Field>}
      <Field label="Notes" className="col-span-2"><Input value={f.notes ?? ""} onChange={set("notes")} /></Field>
      {update.error && <ErrorText error={update.error} />}
      <div className="col-span-2 flex gap-2">
        <Button variant="primary" className="flex-1" busy={update.isPending}>Save</Button>
        <Button type="button" variant="ghost" onClick={onDone}>Cancel</Button>
      </div>
    </form>
  );
}
