import { useEffect, useState } from "react";
import { ApiError } from "@/core/api";
import { Button, ErrorText, Field, Input, Select, Sheet } from "@/core/ui";
import { useCategories, useProduct, useSaveProduct, type Product } from "./api";

type Form = Record<"name" | "brand" | "category" | "barcode" | "defaultUnit" | "minQuantity" | "pantryDays" | "fridgeDays" | "freezerDays", string>;

const toForm = (p: Product): Form => ({
  name: p.name, brand: p.brand ?? "", category: p.category, barcode: p.barcode ?? "", defaultUnit: p.defaultUnit ?? "",
  minQuantity: p.minQuantity?.toString() ?? "", pantryDays: p.pantryDays?.toString() ?? "",
  fridgeDays: p.fridgeDays?.toString() ?? "", freezerDays: p.freezerDays?.toString() ?? "",
});

const num = (s: string) => (s.trim() === "" ? null : Number(s));

/** Edit what a product *is*: name, category, barcode, low-stock minimum and shelf-life overrides. */
export function ProductSheet({ productId, onClose }: { productId: number | null; onClose: () => void }) {
  const product = useProduct(productId);
  const categories = useCategories().data ?? [];
  const save = useSaveProduct();
  const [f, setF] = useState<Form | null>(null);

  useEffect(() => { setF(product.data ? toForm(product.data) : null); save.reset(); }, [product.data, productId]); // eslint-disable-line react-hooks/exhaustive-deps

  const cat = categories.find((c) => c.id === f?.category);
  const err = save.error instanceof ApiError ? save.error : undefined;
  const set = (k: keyof Form) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF((p) => p && { ...p, [k]: e.target.value });
  const days = (v: number | null | undefined) => (v == null ? "not recommended" : `${v} days`);

  return (
    <Sheet open={productId != null} onClose={onClose} title={product.data?.name ?? "Product"}>
      {f && (
        <form className="grid grid-cols-2 gap-3" onSubmit={async (e) => {
          e.preventDefault();
          await save.mutateAsync({
            id: productId!, name: f.name, category: f.category, brand: f.brand || null, barcode: f.barcode || null,
            defaultUnit: f.defaultUnit || null, imageUrl: product.data?.imageUrl ?? null, minQuantity: num(f.minQuantity),
            pantryDays: num(f.pantryDays), fridgeDays: num(f.fridgeDays), freezerDays: num(f.freezerDays),
          });
          onClose();
        }}>
          <Field label="Name" className="col-span-2" error={err?.field("name")}><Input value={f.name} onChange={set("name")} required /></Field>
          <Field label="Category" className="col-span-2">
            <Select value={f.category} onChange={set("category")}>{categories.map((c) => <option key={c.id} value={c.id}>{c.emoji} {c.label}</option>)}</Select>
          </Field>
          <Field label="Brand"><Input value={f.brand} onChange={set("brand")} /></Field>
          <Field label="Default unit"><Input value={f.defaultUnit} onChange={set("defaultUnit")} placeholder="pc, L, bag…" /></Field>
          <Field label="Keep at least" className="col-span-2" error={err?.field("minQuantity")}
            hint="When you have less than this, it goes on the shopping list automatically. Leave blank to not track.">
            <Input inputMode="decimal" value={f.minQuantity} onChange={set("minQuantity")} placeholder="e.g. 2" />
          </Field>
          <Field label="Barcode" className="col-span-2" error={err?.field("barcode")}><Input inputMode="numeric" value={f.barcode} onChange={set("barcode")} /></Field>

          <p className="col-span-2 mt-1 text-xs font-semibold uppercase tracking-wide text-muted">Shelf life (days)</p>
          <p className="col-span-2 -mt-2 text-xs text-muted">Blank uses the {cat?.label ?? "category"} default.</p>
          <div className="col-span-2 grid grid-cols-3 gap-2">
            <Field label="Pantry"><Input inputMode="numeric" value={f.pantryDays} onChange={set("pantryDays")} placeholder={days(cat?.pantryDays)} /></Field>
            <Field label="Fridge"><Input inputMode="numeric" value={f.fridgeDays} onChange={set("fridgeDays")} placeholder={days(cat?.fridgeDays)} /></Field>
            <Field label="Freezer"><Input inputMode="numeric" value={f.freezerDays} onChange={set("freezerDays")} placeholder={days(cat?.freezerDays)} /></Field>
          </div>

          {save.error && <div className="col-span-2"><ErrorText error={save.error} /></div>}
          <div className="col-span-2 flex gap-2">
            <Button variant="primary" className="flex-1" busy={save.isPending}>Save</Button>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
          </div>
        </form>
      )}
    </Sheet>
  );
}
