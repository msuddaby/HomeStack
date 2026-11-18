import { useEffect, useState } from "react";
import { ImagePlus } from "lucide-react";
import { ApiError, errorMessage } from "@/core/api";
import { useModules } from "@/core/modules";
import { shrinkImage } from "@/core/ai";
import { Button, ErrorText, Field, Input, PhotoButton, Select, Sheet } from "@/core/ui";
import { useCategories, useProduct, useRemoveProductImage, useSaveProduct, useSetProductImage, type Product } from "./api";
import { ProductThumb } from "./ProductThumb";

type Form = Record<"name" | "brand" | "category" | "barcode" | "defaultUnit" | "minQuantity" | "pantryDays" | "fridgeDays" | "freezerDays", string>;

const toForm = (p: Product): Form => ({
  name: p.name, brand: p.brand ?? "", category: p.category, barcode: p.barcode ?? "", defaultUnit: p.defaultUnit ?? "",
  minQuantity: p.minQuantity?.toString() ?? "", pantryDays: p.pantryDays?.toString() ?? "",
  fridgeDays: p.fridgeDays?.toString() ?? "", freezerDays: p.freezerDays?.toString() ?? "",
});

const num = (s: string) => (s.trim() === "" ? null : Number(s));

/** Edit what a product *is*: photo, name, category, barcode, low-stock minimum and shelf-life overrides. */
export function ProductSheet({ productId, onClose }: { productId: number | null; onClose: () => void }) {
  const product = useProduct(productId);
  const extras = useModules().flatMap((m) => (m.ProductExtra ? [{ id: m.id, Extra: m.ProductExtra }] : []));
  const categories = useCategories().data ?? [];
  const save = useSaveProduct();
  const setImage = useSetProductImage();
  const removeImage = useRemoveProductImage();
  const [preparing, setPreparing] = useState(false);
  const [photoError, setPhotoError] = useState<string | null>(null);
  const [f, setF] = useState<Form | null>(null);

  // Fill the form once per product, so a photo change (which refetches the product) keeps unsaved edits.
  useEffect(() => { setF(null); save.reset(); setImage.reset(); removeImage.reset(); setPhotoError(null); }, [productId]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { if (product.data && !f) setF(toForm(product.data)); }, [product.data, f]);

  const pickPhoto = async ([file]: File[]) => {
    if (productId == null || !file) return;
    setPhotoError(null);
    setPreparing(true);
    try {
      // Phones take huge photos; the server keeps a ~400px copy, so don't upload more than it needs.
      const photo = await shrinkImage(file, 1024, 0.9);
      setPreparing(false);
      await setImage.mutateAsync({ id: productId, photo });
    } catch (e) {
      setPhotoError(errorMessage(e));
    } finally {
      setPreparing(false);
    }
  };

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
          <div className="col-span-2 flex items-center gap-3">
            {product.data && <ProductThumb product={product.data} emoji={cat?.emoji} className="size-20 rounded-2xl" emojiClassName="text-4xl" lazy={false} />}
            <div className="flex flex-wrap gap-2">
              <PhotoButton onFiles={pickPhoto} busy={preparing || setImage.isPending}>
                {!(preparing || setImage.isPending) && <ImagePlus className="size-4" />}
                {preparing ? "Preparing photo…" : setImage.isPending ? "Uploading…" : product.data?.image ? "Change photo" : "Add photo"}
              </PhotoButton>
              {product.data?.image && (
                <Button type="button" variant="ghost" busy={removeImage.isPending}
                  onClick={() => { setPhotoError(null); removeImage.mutate(productId!, { onError: (e) => setPhotoError(errorMessage(e)) }); }}>
                  Remove
                </Button>
              )}
            </div>
          </div>
          {photoError && <p className="col-span-2 -mt-1 text-sm text-danger">{photoError}</p>}
          {productId != null && extras.map(({ id, Extra }) => <div key={id} className="col-span-2 empty:hidden"><Extra productId={productId} onNavigate={onClose} /></div>)}

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
