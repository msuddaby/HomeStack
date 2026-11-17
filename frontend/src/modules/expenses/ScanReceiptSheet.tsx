import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router";
import { Camera, Check, ImagePlus, Link2, Sparkles, X } from "lucide-react";
import { shrinkImage, useAiSettings } from "@/core/ai";
import { errorMessage } from "@/core/api";
import { todayISO } from "@/core/dates";
import { useToast } from "@/core/toast";
import { Button, Chip, cx, ErrorText, Field, Input, PhotoButton, Select, Sheet, Spinner } from "@/core/ui";
import { useCategories, useLocations, type AddItem, type Category, type Location, type Product } from "@/modules/inventory/api";
import { suggestExpiry } from "@/modules/inventory/logic";
import { readReceipt, useAddReceiptPhotos, useExpenseCategories, useSaveReceipt, type DraftLine, type ItemRef, type ReceiptDraft } from "./api";
import { money } from "./format";

interface FoodRow { add: boolean; name: string; category: string; locationId: number; expiresAt: string; expiryTouched: boolean; product: Product | null }
interface Row {
  key: number; include: boolean; name: string; category: string; amount: string; source: DraftLine;
  /** Linked item: the automatic one, or a suggestion the user said yes to. */
  itemId: number | null;
  /** null = not answered yet (not linked). */
  suggestionAccepted: boolean | null;
  food: FoodRow | null;
}

const num = (s: string) => { const n = Number(s.replace(/[$,\s]/g, "")); return Number.isFinite(n) ? n : null; };

/**
 * Receipt photos → AI → review every line (price, item number, discount; food can go straight into inventory) → save.
 * The one receipt scanner: used from Spending and from Inventory's "Scan receipt".
 */
export function ScanReceiptSheet({ open, onClose, onBack }: { open: boolean; onClose: () => void; onBack?: () => void }) {
  const ai = useAiSettings();
  const expenseCats = useExpenseCategories().data ?? [];
  const foodCats = useCategories().data ?? [];
  const locations = useLocations().data ?? [];
  const foodCatById = useMemo(() => new Map(foodCats.map((c) => [c.id, c])), [foodCats]);
  const save = useSaveReceipt();
  const addPhotos = useAddReceiptPhotos();
  const toast = useToast();
  const navigate = useNavigate();

  const [photos, setPhotos] = useState<{ blob: Blob; url: string }[]>([]);
  const [preparing, setPreparing] = useState(false);
  const [reading, setReading] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [draft, setDraft] = useState<ReceiptDraft | null>(null);
  const [rows, setRows] = useState<Row[]>([]);
  const [store, setStore] = useState("");
  const [date, setDate] = useState(todayISO());
  const [total, setTotal] = useState("");
  const [tax, setTax] = useState("");

  useEffect(() => {
    if (!open) return;
    setPhotos([]); setDraft(null); setRows([]); setError(null); setStore(""); setDate(todayISO()); setTotal(""); setTax("");
    save.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);
  useEffect(() => () => photos.forEach((p) => URL.revokeObjectURL(p.url)), [photos]);

  const locFor = (kind: string): Location | undefined => locations.find((l) => l.kind === kind) ?? locations[0];
  const expiryFor = (f: Pick<FoodRow, "category" | "locationId" | "product">, bought: string) => {
    const loc = locations.find((l) => l.id === f.locationId);
    return loc ? suggestExpiry(foodCatById.get(f.category), loc.kind, bought, null, f.product ?? undefined) ?? "" : "";
  };

  // Shrink as soon as photos are picked, while iOS still lets the page read them.
  async function pickPhotos(files: File[]) {
    setError(null); setPreparing(true);
    try {
      const added: { blob: Blob; url: string }[] = [];
      for (const file of files.slice(0, 4 - photos.length)) {
        const blob = await shrinkImage(file);
        added.push({ blob, url: URL.createObjectURL(blob) });
      }
      setPhotos((p) => [...p, ...added].slice(0, 4));
    } catch (e) { setError(e); } finally { setPreparing(false); }
  }

  async function read() {
    setReading(true); setError(null);
    try {
      const form = new FormData();
      photos.forEach((p) => form.append("images", p.blob, "receipt.jpg"));
      const d = await readReceipt(form);
      const bought = d.purchasedAt ?? todayISO();
      setDraft(d);
      setStore(d.store ?? "");
      setDate(bought);
      setTotal(d.total?.toFixed(2) ?? "");
      setTax(d.tax?.toFixed(2) ?? "");
      setRows(d.lines.map((l) => {
        const food = l.food && (() => {
          const base = { category: l.food.category, locationId: locFor(l.food.location)?.id ?? 0, product: l.food.product };
          return { add: l.food.add, name: l.food.name, expiresAt: expiryFor(base, bought), expiryTouched: false, ...base };
        })();
        return { key: l.key, include: true, name: l.name, category: l.category, amount: l.amount.toFixed(2), source: l,
          itemId: l.item?.id ?? null, suggestionAccepted: null, food };
      }));
    } catch (e) { setError(e); } finally { setReading(false); }
  }

  const update = (key: number, patch: Partial<Row>) => setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  const updateFood = (key: number, patch: Partial<FoodRow>) => setRows((rs) => rs.map((r) => {
    if (r.key !== key || !r.food) return r;
    const food = { ...r.food, ...patch };
    if (!food.expiryTouched && ("category" in patch || "locationId" in patch)) food.expiresAt = expiryFor(food, date);
    return { ...r, food };
  }));
  const changeDate = (d: string) => {
    setDate(d);
    setRows((rs) => rs.map((r) => (r.food && !r.food.expiryTouched ? { ...r, food: { ...r.food, expiresAt: expiryFor(r.food, d || todayISO()) } } : r)));
  };

  const included = rows.filter((r) => r.include && r.name.trim() && num(r.amount) != null);
  const linesTotal = included.reduce((s, r) => s + (num(r.amount) ?? 0), 0);
  const totalNum = num(total);
  const taxNum = num(tax);
  const mismatch = totalNum != null && Math.abs(linesTotal + (taxNum ?? 0) - totalNum) >= 0.05;
  const toInventory = included.filter((r) => r.food?.add && r.food.name.trim() && r.food.locationId).length;

  async function saveAll() {
    if (totalNum == null) return;
    const lines = included.map((r) => {
      const s = r.source;
      const f = r.food;
      const inventory: AddItem | null = f?.add && f.name.trim() && f.locationId ? {
        ...(f.product && f.product.name.toLowerCase() === f.name.trim().toLowerCase() && f.product.category === f.category
          ? { productId: f.product.id } : { product: { name: f.name.trim(), category: f.category, defaultUnit: s.unit } }),
        locationId: f.locationId, quantity: s.quantity, unit: s.unit, purchasedAt: date || null, expiresAt: f.expiresAt || null,
      } : null;
      return {
        itemId: r.suggestionAccepted ? r.source.suggestion!.id : r.itemId, code: s.code, name: r.name.trim(), raw: s.raw,
        category: r.category, quantity: s.quantity, unit: s.unit, unitPrice: s.unitPrice, amount: num(r.amount)!, discount: s.discount, inventory,
        trackInventory: f ? f.add : null,
      };
    });
    const saved = await save.mutateAsync({
      store: store.trim() || null, purchasedAt: date, subtotal: draft?.subtotal ?? null, tax: taxNum, total: totalNum, note: null, lines,
    });
    let photoNote = "";
    try { if (photos.length) await addPhotos.mutateAsync({ id: saved.id, photos: photos.map((p) => p.blob) }); }
    catch (e) { photoNote = ` The photo wasn't saved: ${errorMessage(e)}`; }
    toast({
      message: `Saved ${saved.store ?? "receipt"}, ${money(saved.total)}`
        + (saved.addedToInventory ? `. ${saved.addedToInventory} put away in inventory.` : ".") + photoNote,
      tone: photoNote ? "danger" : undefined,
      action: { label: "View", onClick: () => navigate(`/expenses/receipts/${saved.id}`) },
    });
    onClose();
  }

  const reviewing = !!draft;
  const footer = !ai.data?.configured ? undefined : reviewing ? (
    <div className="flex gap-2">
      <Button className="shrink-0" onClick={() => { setDraft(null); setRows([]); }}>Back</Button>
      <Button className="flex-1" variant="primary" disabled={!included.length || totalNum == null || !date}
        busy={save.isPending || addPhotos.isPending} onClick={saveAll}>
        <Check className="size-4" />Save {money(totalNum ?? linesTotal)}{toInventory ? ` · ${toInventory} to inventory` : ""}
      </Button>
    </div>
  ) : (
    <div className="flex gap-2">
      {onBack && <Button className="shrink-0" onClick={onBack}>Back</Button>}
      <Button className="flex-1" variant="primary" busy={reading} disabled={!photos.length || preparing} onClick={read}>
        <Sparkles className="size-4" />{reading ? "Reading…" : "Read receipt"}
      </Button>
    </div>
  );

  return (
    <Sheet open={open} onClose={onClose} title={reviewing ? (store || "Check the receipt") : "Scan a receipt"} footer={footer}>
      {!ai.data ? <Spinner /> : !ai.data.configured ? (
        <div className="flex flex-col items-start gap-3 text-sm">
          <p>Reading receipts uses an AI service. Pick one (DeepSeek, Claude, a local Ollama model…) in Settings first.</p>
          <Link to="/settings" onClick={onClose}><Button variant="primary"><Sparkles className="size-4" />Set up AI</Button></Link>
        </div>
      ) : reading ? (
        <div className="flex flex-col items-center gap-3 py-10 text-center text-sm text-muted">
          <Spinner />
          <p>Reading your receipt… usually 15–45 seconds for a long one.</p>
        </div>
      ) : reviewing ? (
        <div className="flex flex-col gap-3">
          {draft.duplicateOf != null && (
            <p className="rounded-xl bg-warn-soft p-3 text-sm text-warn">
              Looks like this receipt is already saved (same store, date and total).{" "}
              <Link to={`/expenses/receipts/${draft.duplicateOf}`} onClick={onClose} className="underline">See it</Link>. Saving again counts it twice.
            </p>
          )}
          {draft.note && <p className="rounded-xl bg-warn-soft p-3 text-sm text-warn">{draft.note}</p>}
          <div className="grid grid-cols-[1fr_1fr] gap-x-2 gap-y-3">
            <Field label="Store" className="col-span-2"><Input value={store} onChange={(e) => setStore(e.target.value)} /></Field>
            <Field label="Date" className="col-span-2"><Input type="date" value={date} max={todayISO()} onChange={(e) => changeDate(e.target.value)} /></Field>
            <Field label="Tax"><Input inputMode="decimal" value={tax} onChange={(e) => setTax(e.target.value)} placeholder="0.00" /></Field>
            <Field label="Total"><Input inputMode="decimal" value={total} onChange={(e) => setTotal(e.target.value)} placeholder="0.00" required /></Field>
          </div>
          {mismatch && (
            <p className="text-xs text-warn">
              Lines add up to {money(linesTotal)}{taxNum ? ` + ${money(taxNum)} tax` : ""}, but the total is {money(totalNum!)}.
              A line may be misread or missing; the total is what counts toward spending.
            </p>
          )}
          {!rows.length && <p className="text-sm text-muted">No lines found. Try a clearer photo.</p>}

          <ul className="flex flex-col gap-2">
            {rows.map((r) => (
              <LineRow key={r.key} r={r} expenseCats={expenseCats} foodCats={foodCats} locations={locations}
                update={(p) => update(r.key, p)} updateFood={(p) => updateFood(r.key, p)} />
            ))}
          </ul>
          <p className="text-xs text-muted">Check names, prices and places. AI can misread receipts. Untick a line to leave it out.</p>
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {photos.length > 0 && (
            <div className="flex gap-2 overflow-x-auto pb-1">
              {photos.map((p, i) => (
                <div key={p.url} className="relative shrink-0">
                  <img src={p.url} alt={`Receipt photo ${i + 1}`} className="h-36 w-24 rounded-xl border border-border object-cover" />
                  <button type="button" aria-label="Remove photo" onClick={() => setPhotos((ps) => ps.filter((x) => x !== p))}
                    className="absolute -right-1.5 -top-1.5 flex size-6 items-center justify-center rounded-full bg-text text-surface"><X className="size-3.5" /></button>
                </div>
              ))}
            </div>
          )}
          {preparing ? (
            <div className="flex min-h-28 items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-border text-sm text-muted">
              <Spinner />Preparing photo…
            </div>
          ) : photos.length < 4 && (
            <div className="grid grid-cols-2 gap-2">
              <PhotoButton bare camera onFiles={pickPhotos}
                className="flex min-h-28 flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-border text-sm font-medium">
                <Camera className="size-7 text-accent" />{photos.length ? "Another photo" : "Take photo"}
              </PhotoButton>
              <PhotoButton bare multiple onFiles={pickPhotos}
                className="flex min-h-28 flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-border text-sm font-medium">
                <ImagePlus className="size-7 text-accent" />From photos
              </PhotoButton>
            </div>
          )}
          <p className="text-xs text-muted">
            Flat, well lit, whole width in frame. Long receipt? One photo per section, top to bottom (up to 4).
            Every line is recorded with its price; food can go straight into inventory.
          </p>
          {error != null && <ErrorText error={error} />}
        </div>
      )}
      {reviewing && (save.error || error) ? <div className="mt-3"><ErrorText error={save.error ?? error} /></div> : null}
    </Sheet>
  );
}

function LineRow({ r, expenseCats, foodCats, locations, update, updateFood }: {
  r: Row; expenseCats: { id: string; label: string; emoji: string }[]; foodCats: Category[]; locations: Location[];
  update: (p: Partial<Row>) => void; updateFood: (p: Partial<FoodRow>) => void;
}) {
  const s = r.source;
  return (
    <li className={cx("rounded-2xl border border-border p-3", !r.include && "opacity-50")}>
      <div className="flex items-center gap-2">
        <input type="checkbox" checked={r.include} onChange={(e) => update({ include: e.target.checked })}
          aria-label={`Include ${r.name}`} className="size-5 shrink-0 accent-[var(--color-accent)]" />
        <Input value={r.name} onChange={(e) => update({ name: e.target.value })} className="min-h-10 min-w-0 flex-1 font-medium" aria-label="Name" />
        <Input inputMode="decimal" value={r.amount} onChange={(e) => update({ amount: e.target.value })} className="min-h-10 w-20 shrink-0 text-right" aria-label="Amount" />
      </div>
      {r.include && (
        <>
          <div className="mt-2 flex flex-wrap items-center gap-1.5 text-xs text-muted">
            <Select value={r.category} onChange={(e) => update({ category: e.target.value })} className="min-h-8 w-auto py-0 text-xs" aria-label="Spending category">
              {expenseCats.map((c) => <option key={c.id} value={c.id}>{c.emoji} {c.label}</option>)}
            </Select>
            {s.quantity !== 1 && <span>×{s.quantity}{s.unit ? ` ${s.unit}` : ""}</span>}
            {s.discount != null && <Chip tone="accent">−{money(s.discount)} off</Chip>}
            {s.code && <span className="font-mono">#{s.code}</span>}
          </div>
          {s.item ? (
            <p className="mt-1.5 flex items-center gap-1 text-xs text-accent"><Link2 className="size-3.5" />{boughtBefore(s.item)}</p>
          ) : s.suggestion && (
            <div className="mt-1.5 flex flex-wrap items-center gap-2 rounded-xl bg-surface-2 px-2 py-1.5 text-xs">
              <span className="min-w-0 flex-1">Same as <b>{s.suggestion.name}</b>{s.suggestion.lastStore ? ` (${s.suggestion.lastStore}${s.suggestion.lastUnitPrice != null ? `, ${money(s.suggestion.lastUnitPrice)}` : ""})` : ""}?</span>
              {/* Linking to the item also takes on its "keep in inventory?" choice; saying no goes back to the AI's start. */}
              <Button type="button" variant={r.suggestionAccepted === true ? "primary" : "secondary"} className="min-h-8 px-3 text-xs"
                onClick={() => { update({ suggestionAccepted: true }); if (r.food) updateFood({ add: !s.suggestion!.skipInventory }); }}>Yes</Button>
              <Button type="button" variant={r.suggestionAccepted === false ? "primary" : "secondary"} className="min-h-8 px-3 text-xs"
                onClick={() => { update({ suggestionAccepted: false }); if (r.food && s.food) updateFood({ add: s.food.add }); }}>No</Button>
            </div>
          )}
          {r.food && (
            <div className="mt-2 rounded-xl bg-surface-2 p-2">
              <label className="flex items-center gap-2 text-xs font-medium">
                <input type="checkbox" checked={r.food.add} onChange={(e) => updateFood({ add: e.target.checked })} className="size-4 accent-[var(--color-accent)]" />
                Put away in inventory {r.food.product && <Chip tone="accent">Known item</Chip>}
              </label>
              {!r.food.add && (s.item?.skipInventory || (r.suggestionAccepted && s.suggestion?.skipInventory)) && (
                <p className="mt-1 text-[11px] text-muted">Left out last time. Spending is still tracked.</p>
              )}
              {r.food.add && (
                <div className="mt-2 grid grid-cols-2 gap-2">
                  <Input value={r.food.name} onChange={(e) => updateFood({ name: e.target.value })} className="col-span-2 min-h-9" aria-label="Inventory name" />
                  <Select value={r.food.locationId} onChange={(e) => updateFood({ locationId: Number(e.target.value) })} className="min-h-9" aria-label="Where">
                    {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
                  </Select>
                  <Input type="date" value={r.food.expiresAt} onChange={(e) => updateFood({ expiresAt: e.target.value, expiryTouched: true })}
                    className="min-h-9" aria-label="Expires" />
                  <Select value={r.food.category} onChange={(e) => updateFood({ category: e.target.value })} className="col-span-2 min-h-9" aria-label="Food category">
                    {foodCats.map((c) => <option key={c.id} value={c.id}>{c.emoji} {c.label}</option>)}
                  </Select>
                </div>
              )}
            </div>
          )}
          {s.raw && s.raw !== r.name && <p className="mt-1 truncate font-mono text-[11px] text-muted">{s.raw}</p>}
        </>
      )}
    </li>
  );
}

function boughtBefore(i: ItemRef) {
  return `Bought before${i.lastUnitPrice != null ? `: last ${money(i.lastUnitPrice)}` : ""}${i.lastStore ? ` at ${i.lastStore}` : ""}`;
}
