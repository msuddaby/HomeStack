import { useEffect, useState } from "react";
import { Trash2 } from "lucide-react";
import { Button, ErrorText, Field, Input, Select, Sheet } from "@/core/ui";
import { useCategories } from "@/modules/inventory/api";
import { useRemoveListItem, useUpdateListItem, type ShoppingItem } from "./api";

export function EditListItemSheet({ item, onClose }: { item: ShoppingItem | null; onClose: () => void }) {
  const categories = useCategories().data ?? [];
  const update = useUpdateListItem();
  const remove = useRemoveListItem();
  const [f, setF] = useState({ name: "", category: "other", quantity: "", unit: "", note: "" });

  useEffect(() => {
    if (item) setF({ name: item.name, category: item.category, quantity: item.quantity?.toString() ?? "", unit: item.unit ?? "", note: item.note ?? "" });
    update.reset(); remove.reset();
  }, [item?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });

  return (
    <Sheet open={!!item} onClose={onClose} title={item?.name ?? ""}>
      {item && (
        <form className="grid grid-cols-2 gap-3" onSubmit={async (e) => {
          e.preventDefault();
          await update.mutateAsync({ id: item.id, name: f.name, category: f.category, quantity: f.quantity ? Number(f.quantity) : null, unit: f.unit || null, note: f.note || null });
          onClose();
        }}>
          <Field label="Name" className="col-span-2"><Input value={f.name} onChange={set("name")} required disabled={!!item.productId} /></Field>
          <Field label="Quantity"><Input inputMode="decimal" value={f.quantity} onChange={set("quantity")} placeholder="Any" /></Field>
          <Field label="Unit"><Input value={f.unit} onChange={set("unit")} /></Field>
          <Field label="Note" className="col-span-2"><Input value={f.note} onChange={set("note")} placeholder="e.g. the lactose-free one" /></Field>
          <Field label="Section" className="col-span-2" hint={item.productId ? "Linked to an inventory product" : undefined}>
            <Select value={f.category} onChange={set("category")} disabled={!!item.productId}>
              {categories.map((c) => <option key={c.id} value={c.id}>{c.emoji} {c.label}</option>)}
            </Select>
          </Field>
          {item.source === "lowStock" && (
            <p className="col-span-2 text-xs text-muted">Added automatically because you're running low. Removing it hides it until you restock.</p>
          )}
          <div className="col-span-2"><ErrorText error={update.error ?? remove.error} /></div>
          <div className="col-span-2 flex gap-2">
            <Button variant="primary" className="flex-1" busy={update.isPending}>Save</Button>
            <Button type="button" variant="danger" busy={remove.isPending} onClick={async () => { await remove.mutateAsync(item.id); onClose(); }}>
              <Trash2 className="size-4" />Remove
            </Button>
          </div>
        </form>
      )}
    </Sheet>
  );
}
