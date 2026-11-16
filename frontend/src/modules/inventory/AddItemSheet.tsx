import { useCallback, useDeferredValue, useEffect, useMemo, useRef, useState } from "react";
import { Check, ChevronDown, PenLine, Receipt, ScanBarcode } from "lucide-react";
import { ApiError, errorMessage } from "@/core/api";
import { todayISO } from "@/core/dates";
import { Button, Chip, cx, ErrorText, Field, Input, Sheet } from "@/core/ui";
import { lookupBarcode, useAddItem, useCategories, useLocations, useProductSearch, type Product } from "./api";
import { BarcodeScanner } from "./BarcodeScanner";
import { ReadSheet, type ReadMode } from "./ReadSheet";
import { guessCategory, suggestExpiry } from "./logic";

interface Form {
  name: string;
  product: Product | null;
  category: string;
  categoryTouched: boolean;
  brand: string;
  barcode: string;
  imageUrl: string;
  locationId: number | null;
  quantity: string;
  unit: string;
  purchasedAt: string;
  expiresAt: string;
  expiryTouched: boolean;
  noExpiry: boolean;
  frozenAt: string;
  notes: string;
  price: string;
}

const blank = (locationId: number | null): Form => ({
  name: "", product: null, category: "", categoryTouched: false, brand: "", barcode: "", imageUrl: "", locationId, quantity: "1", unit: "",
  purchasedAt: todayISO(), expiresAt: "", expiryTouched: false, noExpiry: false, frozenAt: "", notes: "", price: "",
});

export function AddItemSheet({ open, onClose, defaultLocationId }: { open: boolean; onClose: () => void; defaultLocationId?: number }) {
  const categories = useCategories().data ?? [];
  const locations = useLocations().data ?? [];
  const add = useAddItem();
  const [f, setF] = useState<Form>(() => blank(defaultLocationId ?? null));
  const [locationTouched, setLocationTouched] = useState(false);
  const [showMore, setShowMore] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [reading, setReading] = useState<ReadMode | null>(null);
  const [scanNote, setScanNote] = useState<string | null>(null);
  const [added, setAdded] = useState<string[]>([]);
  const nameRef = useRef<HTMLInputElement>(null);

  const set = <K extends keyof Form>(k: K, v: Form[K]) => setF((p) => ({ ...p, [k]: v }));

  useEffect(() => {
    if (open) { setF(blank(defaultLocationId ?? null)); setLocationTouched(!!defaultLocationId); setAdded([]); setScanNote(null); setReading(null); add.reset(); }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const category = categories.find((c) => c.id === f.category);
  const location = locations.find((l) => l.id === f.locationId) ?? null;
  const isFreezer = location?.kind === "freezer";

  // Pick a sensible default location for the category until the user chooses one.
  useEffect(() => {
    if (locationTouched || !locations.length) return;
    const kind = category?.defaultLocation ?? "pantry";
    const loc = locations.find((l) => l.kind === kind) ?? locations[0]!;
    if (loc.id !== f.locationId) set("locationId", loc.id);
  }, [category?.id, locations, locationTouched]); // eslint-disable-line react-hooks/exhaustive-deps

  const suggested = useMemo(() => {
    if (!location || !f.category) return null;
    return suggestExpiry(category, location.kind, f.purchasedAt || todayISO(), isFreezer ? f.frozenAt || null : null, f.product ?? undefined);
  }, [category, location, isFreezer, f.category, f.purchasedAt, f.frozenAt, f.product]);

  useEffect(() => {
    if (!f.expiryTouched) set("expiresAt", suggested ?? "");
  }, [suggested, f.expiryTouched]);

  // Autocomplete from products we've seen before.
  const q = useDeferredValue(f.product ? "" : f.name);
  const matches = useProductSearch(q).data ?? [];
  const showMatches = !f.product && f.name.trim().length > 0 && matches.length > 0;

  function pickProduct(p: Product) {
    setF((prev) => ({ ...prev, name: p.name, product: p, category: p.category, brand: p.brand ?? "", unit: p.defaultUnit ?? prev.unit, expiryTouched: false }));
  }

  function onNameChange(name: string) {
    // Guess the category from the name until the user picks one themselves.
    setF((prev) => ({ ...prev, name, product: null, category: prev.categoryTouched ? prev.category : guessCategory(name) ?? "" }));
  }

  const onDetected = useCallback(async (code: string) => {
    setScanning(false);
    setScanNote("Looking up…");
    try {
      const r = await lookupBarcode(code);
      if (r.product) { pickProduct(r.product); setScanNote(`Found ${r.product.name}`); }
      else if (r.suggestion) {
        const s = r.suggestion;
        setF((prev) => ({ ...prev, product: null, name: s.name, brand: s.brand ?? "", category: s.category, categoryTouched: true, imageUrl: s.imageUrl ?? "", barcode: code, expiryTouched: false }));
        setScanNote(`Found in Open Food Facts${s.quantity ? ` · ${s.quantity}` : ""}. Check the details.`);
      } else {
        setF((prev) => ({ ...prev, product: null, barcode: code }));
        setScanNote("Not found. Enter a name and we'll remember this barcode.");
        nameRef.current?.focus();
      }
    } catch (e) { setScanNote(errorMessage(e)); }
  }, []);

  async function submit(again: boolean) {
    if (!f.locationId) return;
    const quantity = Number(f.quantity) || 1;
    const body = {
      ...(f.product ? { productId: f.product.id } : {
        product: { name: f.name.trim(), category: f.category || "other", brand: f.brand || null, barcode: f.barcode || null, imageUrl: f.imageUrl || null, defaultUnit: f.unit || null },
      }),
      locationId: f.locationId,
      quantity,
      unit: f.unit || null,
      purchasedAt: f.purchasedAt || null,
      expiresAt: f.noExpiry ? null : f.expiresAt || null,
      noExpiry: f.noExpiry || !f.expiresAt,
      frozenAt: isFreezer ? f.frozenAt || null : null,
      notes: f.notes || null,
      price: f.price.trim() === "" ? null : Number(f.price),
    };
    await add.mutateAsync(body);
    if (!again) return onClose();
    setAdded((a) => [f.name.trim(), ...a]);
    // Keep location and purchase date for the next item in the same shopping trip.
    setF((prev) => ({ ...blank(prev.locationId), purchasedAt: prev.purchasedAt }));
    setScanNote(null);
    nameRef.current?.focus();
  }

  const err = add.error instanceof ApiError ? add.error : undefined;
  const canSubmit = f.name.trim().length > 0 && !!f.locationId && (!!f.product || !!f.category);

  return (
    <>
      <Sheet open={open && !scanning && !reading} onClose={onClose} title="Add to inventory"
        footer={
          <div className="flex gap-2">
            <Button className="flex-1" disabled={!canSubmit} busy={add.isPending} onClick={() => submit(true)}>Add &amp; next</Button>
            <Button className="flex-1" variant="primary" disabled={!canSubmit} busy={add.isPending} onClick={() => submit(false)}>Add</Button>
          </div>
        }>
        <form className="flex flex-col gap-4" onSubmit={(e) => { e.preventDefault(); if (canSubmit) submit(true); }}>
          {added.length === 0 && !f.name && (
            <div className="grid grid-cols-2 gap-2">
              <Button type="button" onClick={() => setReading("receipt")}><Receipt className="size-4" />Scan receipt</Button>
              <Button type="button" onClick={() => setReading("text")}><PenLine className="size-4" />Type a list</Button>
            </div>
          )}
          {added.length > 0 && (
            <div className="flex flex-wrap gap-1">{added.slice(0, 6).map((n, i) => <Chip key={i} tone="accent"><Check className="size-3" />{n}</Chip>)}</div>
          )}

          <div className="relative">
            <Field label="What is it?" error={err?.field("product.name")}>
              <div className="flex gap-2">
                <Input ref={nameRef} autoFocus placeholder="e.g. Milk, chicken thighs" value={f.name} onChange={(e) => onNameChange(e.target.value)} autoComplete="off" />
                <Button type="button" onClick={() => setScanning(true)} aria-label="Scan barcode" className="shrink-0 px-3"><ScanBarcode className="size-5" /></Button>
              </div>
            </Field>
            {showMatches && (
              <ul className="absolute inset-x-0 top-full z-10 mt-1 overflow-hidden rounded-xl border border-border bg-surface shadow-lg">
                {matches.map((p) => (
                  <li key={p.id}>
                    <button type="button" onClick={() => pickProduct(p)} className="flex w-full items-center gap-2 px-3 py-2.5 text-left hover:bg-surface-2">
                      <span>{categories.find((c) => c.id === p.category)?.emoji}</span>
                      <span className="flex-1 truncate">{p.name}</span>
                      {p.brand && <span className="truncate text-xs text-muted">{p.brand}</span>}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
          {scanNote && <p className="-mt-2 text-xs text-muted">{scanNote}</p>}

          {!f.product && (
            <div>
              <span className="mb-1 block text-xs font-medium text-muted">Category</span>
              <div className="flex flex-wrap gap-1.5">
                {categories.map((c) => (
                  <button type="button" key={c.id} onClick={() => setF((p) => ({ ...p, category: c.id, categoryTouched: true, expiryTouched: false }))}
                    className={cx("rounded-full border px-2.5 py-1 text-xs", f.category === c.id ? "border-accent bg-accent-soft text-accent" : "border-border text-muted")}>
                    {c.emoji} {c.label}
                  </button>
                ))}
              </div>
            </div>
          )}

          <div className="grid grid-cols-2 gap-3">
            <Field label="Where" className="col-span-2">
              <div className="flex flex-wrap gap-1.5">
                {locations.map((l) => (
                  <button type="button" key={l.id} onClick={() => { setLocationTouched(true); set("locationId", l.id); }}
                    className={cx("min-h-10 rounded-xl border px-3 text-sm", f.locationId === l.id ? "border-accent bg-accent-soft font-medium text-accent" : "border-border")}>
                    {l.name}
                  </button>
                ))}
              </div>
            </Field>
            <Field label="Quantity"><Input inputMode="decimal" value={f.quantity} onChange={(e) => set("quantity", e.target.value)} /></Field>
            <Field label="Unit"><Input placeholder="pc, kg, L…" value={f.unit} onChange={(e) => set("unit", e.target.value)} list="units" /></Field>
            <datalist id="units">{["pc", "pack", "g", "kg", "lb", "oz", "mL", "L", "can", "jar", "bottle", "bag", "box"].map((u) => <option key={u} value={u} />)}</datalist>

            {isFreezer && (
              <Field label="Date frozen" hint="If it was in the fridge first, when it went in">
                <Input type="date" value={f.frozenAt || f.purchasedAt} max={todayISO()} onChange={(e) => setF((p) => ({ ...p, frozenAt: e.target.value, expiryTouched: false }))} />
              </Field>
            )}
            <Field label={isFreezer ? "Best before" : "Expires"} className={isFreezer ? "" : "col-span-2"}
              hint={f.noExpiry ? "Not tracked" : !f.expiryTouched && suggested ? "Suggested from category. Change it if the package says otherwise" : undefined}>
              <Input type="date" disabled={f.noExpiry} value={f.expiresAt} onChange={(e) => setF((p) => ({ ...p, expiresAt: e.target.value, expiryTouched: true }))} />
            </Field>
          </div>

          <button type="button" onClick={() => setShowMore((s) => !s)} className="flex items-center gap-1 self-start text-sm text-muted">
            <ChevronDown className={cx("size-4 transition", showMore && "rotate-180")} />More options
          </button>
          {showMore && (
            <div className="grid grid-cols-2 gap-3">
              <Field label="Purchased"><Input type="date" value={f.purchasedAt} onChange={(e) => setF((p) => ({ ...p, purchasedAt: e.target.value, expiryTouched: false }))} /></Field>
              <Field label="Price ($)" error={err?.field("price")} hint="Counts toward waste stats if it's thrown out">
                <Input inputMode="decimal" placeholder="Optional" value={f.price} onChange={(e) => set("price", e.target.value)} />
              </Field>
              {!f.product && <Field label="Brand"><Input value={f.brand} onChange={(e) => set("brand", e.target.value)} /></Field>}
              <label className="col-span-2 flex items-center gap-2 text-sm">
                <input type="checkbox" checked={f.noExpiry} onChange={(e) => set("noExpiry", e.target.checked)} className="size-4 accent-[var(--color-accent)]" />
                No expiry date
              </label>
              <Field label="Notes" className="col-span-2"><Input value={f.notes} onChange={(e) => set("notes", e.target.value)} /></Field>
            </div>
          )}
          {add.error && !err?.errors && <ErrorText error={add.error} />}
          {err?.errors && !err.field("product.name") && <ErrorText error={add.error} />}
          <button type="submit" hidden />
        </form>
      </Sheet>
      <BarcodeScanner open={open && scanning} onClose={() => setScanning(false)} onDetected={onDetected} />
      <ReadSheet open={open && !!reading} mode={reading ?? "receipt"} onBack={() => setReading(null)} onDone={onClose} />
    </>
  );
}
