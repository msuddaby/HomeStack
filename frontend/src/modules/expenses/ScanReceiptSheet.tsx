import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router";
import { Camera, Check, FileText, ImagePlus, Link2, Sparkles, X } from "lucide-react";
import { shrinkImage, useAiSettings } from "@/core/ai";
import { errorMessage } from "@/core/api";
import { parseISO, todayISO } from "@/core/dates";
import { useToast } from "@/core/toast";
import { Button, Chip, cx, ErrorText, Field, Input, PhotoButton, Select, Sheet, Spinner } from "@/core/ui";
import { useCategories, useLocations, type AddItem, type Category, type Location, type Product } from "@/modules/inventory/api";
import { guessCategory, suggestExpiry } from "@/modules/inventory/logic";
import { readReceipt, useAddReceiptPhotos, useAttachDocument, useExpenseCategories, usePeople, useSaveReceipt, type DraftLine, type ItemRef, type PackSource, type Person, type ReceiptDraft } from "./api";
import { money } from "./format";
import { noSplit, PeopleChips, SplitEditor, toParts, type SplitValue } from "./Split";

interface FoodRow {
  add: boolean; name: string; category: string; locationId: number; expiresAt: string; expiryTouched: boolean; product: Product | null;
  /** "Put away as": how much goes into inventory. Follows the line's quantity (× perOne) until edited. */
  qty: string; unit: string; qtyTouched: boolean; perOne: number; from: PackSource;
  /** Already put away on this trip (from the shopping list). */
  already: NonNullable<DraftLine["food"]>["alreadyPutAway"];
}
interface Row {
  key: number; include: boolean; name: string; category: string; amount: string; source: DraftLine;
  /** As bought on the receipt (2 jars, 2.1 kg): used for the price of one. */
  quantity: string; unit: string;
  /** Linked item: the automatic one, or a suggestion the user said yes to. */
  itemId: number | null;
  /** null = not answered yet (not linked). */
  suggestionAccepted: boolean | null;
  food: FoodRow | null;
  /** Who this line is split between, when not the receipt's split (snacks for two, a shirt for one). Null: the receipt's. */
  split: number[] | null;
}

/** Spending categories that are food and drink, so the line can be put away in inventory. */
const FOOD_SPENDING = new Set(["groceries", "snacks", "alcohol"]);

const num = (s: string) => { const n = Number(s.replace(/[$,\s]/g, "")); return s.trim() !== "" && Number.isFinite(n) ? n : null; };
const round3 = (n: number) => String(Math.round(n * 1000) / 1000);

/**
 * Receipt photos or a PDF (an online order) → AI → review every line (price, item number, discount; food can go straight into inventory) → save.
 * The one receipt scanner: used from Money and from Inventory's "Scan receipt".
 */
export function ScanReceiptSheet({ open, onClose, onBack }: { open: boolean; onClose: () => void; onBack?: () => void }) {
  const ai = useAiSettings();
  const expenseCats = useExpenseCategories().data ?? [];
  const foodCats = useCategories().data ?? [];
  const locations = useLocations().data ?? [];
  const foodCatById = useMemo(() => new Map(foodCats.map((c) => [c.id, c])), [foodCats]);
  const save = useSaveReceipt();
  const addPhotos = useAddReceiptPhotos();
  const attach = useAttachDocument();
  const toast = useToast();
  const navigate = useNavigate();

  const [photos, setPhotos] = useState<{ blob: Blob; url: string }[]>([]);
  /** An online order's PDF receipt, read instead of photos and kept with the receipt. */
  const [pdf, setPdf] = useState<{ blob: Blob; name: string } | null>(null);
  const [preparing, setPreparing] = useState(false);
  const [reading, setReading] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [draft, setDraft] = useState<ReceiptDraft | null>(null);
  const [rows, setRows] = useState<Row[]>([]);
  const [store, setStore] = useState("");
  const [date, setDate] = useState(todayISO());
  const [total, setTotal] = useState("");
  const [tax, setTax] = useState("");
  const people = usePeople().data ?? [];
  const myId = people.find((p) => p.isMe)?.id ?? null;
  const [split, setSplit] = useState<SplitValue>(noSplit(null));

  useEffect(() => {
    if (!open) return;
    setPhotos([]); setPdf(null); setDraft(null); setRows([]); setError(null); setStore(""); setDate(todayISO()); setTotal(""); setTax("");
    setSplit(noSplit(myId));
    save.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);
  // Paid by me, once the people list has loaded.
  useEffect(() => { if (myId != null) setSplit((s) => (s.paidBy == null ? { ...s, paidBy: myId } : s)); }, [myId]);
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

  async function pickPdf(files: File[]) {
    const file = files[0];
    if (!file) return;
    setError(null);
    setPhotos([]);
    setPdf({ blob: new Blob([await file.arrayBuffer()], { type: "application/pdf" }), name: file.name || "receipt.pdf" });
  }

  async function read() {
    setReading(true); setError(null);
    try {
      const form = new FormData();
      if (pdf) form.append("images", pdf.blob, pdf.name);
      else photos.forEach((p) => form.append("images", p.blob, "receipt.jpg"));
      const d = await readReceipt(form);
      const bought = d.purchasedAt ?? todayISO();
      setDraft(d);
      setStore(d.store ?? "");
      setDate(bought);
      setTotal(d.total?.toFixed(2) ?? "");
      setTax(d.tax?.toFixed(2) ?? "");
      setRows(d.lines.map((l) => {
        const food = l.food && ((): FoodRow => {
          const base = { category: l.food.category, locationId: locFor(l.food.location)?.id ?? 0, product: l.food.product };
          return { add: l.food.add, name: l.food.name, expiresAt: expiryFor(base, bought), expiryTouched: false, ...base, ...draftAmount(l),
            already: l.food.alreadyPutAway };
        })();
        return { key: l.key, include: true, name: l.name, category: l.category, amount: l.amount.toFixed(2), source: l,
          quantity: round3(l.quantity), unit: l.unit ?? "", itemId: l.item?.id ?? null, suggestionAccepted: null, food, split: null };
      }));
    } catch (e) { setError(e); } finally { setReading(false); }
  }

  const update = (key: number, patch: Partial<Row>) => setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  // Buying 2 instead of 1: the put-away amount doubles too, unless it was typed in.
  const updateQuantity = (key: number, quantity: string) => setRows((rs) => rs.map((r) => {
    if (r.key !== key) return r;
    const q = num(quantity);
    const food = r.food && !r.food.qtyTouched && q != null && q > 0 ? { ...r.food, qty: round3(q * r.food.perOne) } : r.food;
    return { ...r, quantity, food };
  }));
  // The reader said "not food" but it is (or the other way round): moving a line into Groceries offers to put it away,
  // and moving it out drops the put-away it was only given for that.
  const changeCategory = (key: number, category: string) => setRows((rs) => rs.map((r) => {
    if (r.key !== key) return r;
    if (!FOOD_SPENDING.has(category)) return { ...r, category, food: r.source.food ? r.food : null };
    if (r.food) return { ...r, category };
    const foodCat = foodCatById.get(guessCategory(r.name) ?? "other") ?? foodCats[0];
    const q = num(r.quantity) ?? r.source.quantity;
    const base = { category: foodCat?.id ?? "other", locationId: locFor(foodCat?.defaultLocation ?? "pantry")?.id ?? 0, product: null };
    const food: FoodRow = { add: true, name: r.name, expiresAt: expiryFor(base, date || todayISO()), expiryTouched: false, ...base,
      qty: round3(q), unit: r.unit, qtyTouched: false, perOne: 1, from: "receipt", already: null };
    return { ...r, category, food };
  }));
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
      const quantity = num(r.quantity) ?? s.quantity;
      const unit = r.unit.trim() || null;
      const putAway = (f && num(f.qty)) || quantity;
      const putAwayUnit = f ? f.unit.trim() || null : unit;
      const inventory: AddItem | null = f?.add && f.name.trim() && f.locationId ? {
        ...(f.product && f.product.name.toLowerCase() === f.name.trim().toLowerCase() && f.product.category === f.category
          ? { productId: f.product.id } : { product: { name: f.name.trim(), category: f.category, defaultUnit: putAwayUnit } }),
        locationId: f.locationId, quantity: putAway, unit: putAwayUnit, purchasedAt: date || null, expiresAt: f.expiresAt || null,
      } : null;
      return {
        itemId: r.suggestionAccepted ? r.source.suggestion!.id : r.itemId, code: s.code, name: r.name.trim(), raw: s.raw,
        category: r.category, quantity, unit, unitPrice: s.unitPrice, amount: num(r.amount)!, discount: s.discount, inventory,
        // Left out because it's already put away isn't "don't keep this in inventory": don't remember it as that.
        trackInventory: f && !(f.already && !f.add) ? f.add : null,
        split: r.split?.length ? r.split.map((personId) => ({ personId, weight: 1 })) : null,
        pricesInventoryItemId: f?.already && !f.add ? f.already.itemId : null,
      };
    });
    const saved = await save.mutateAsync({
      store: store.trim() || null, purchasedAt: date, subtotal: draft?.subtotal ?? null, tax: taxNum, total: totalNum, note: null, lines,
      paidByPersonId: split.paidBy, split: toParts(split),
    });
    let photoNote = "";
    try {
      if (pdf) await attach.mutateAsync({ id: saved.id, file: pdf.blob, name: pdf.name });
      else if (photos.length) await addPhotos.mutateAsync({ id: saved.id, photos: photos.map((p) => p.blob) });
    } catch (e) { photoNote = ` The ${pdf ? "PDF" : "photo"} wasn't saved: ${errorMessage(e)}`; }
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
        busy={save.isPending || addPhotos.isPending || attach.isPending} onClick={saveAll}>
        <Check className="size-4" />Save {money(totalNum ?? linesTotal)}{toInventory ? ` · ${toInventory} to inventory` : ""}
      </Button>
    </div>
  ) : (
    <div className="flex gap-2">
      {onBack && <Button className="shrink-0" onClick={onBack}>Back</Button>}
      <Button className="flex-1" variant="primary" busy={reading} disabled={(!photos.length && !pdf) || preparing} onClick={read}>
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
          {people.length > 1 && <SplitEditor people={people} value={{ ...split, custom: null }} onChange={(v) => setSplit({ ...v, custom: null })} />}

          <ul className="flex flex-col gap-2">
            {rows.map((r) => (
              <LineRow key={r.key} r={r} expenseCats={expenseCats} foodCats={foodCats} locations={locations}
                people={people} receiptSplit={split.people}
                update={(p) => update(r.key, p)} changeCategory={(c) => changeCategory(r.key, c)} updateFood={(p) => updateFood(r.key, p)} updateQuantity={(q) => updateQuantity(r.key, q)} />
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
          ) : pdf ? (
            <div className="flex items-center gap-3 rounded-2xl border border-border p-3 text-sm">
              <FileText className="size-6 shrink-0 text-accent" />
              <span className="min-w-0 flex-1 truncate">{pdf.name}</span>
              <button type="button" aria-label="Remove PDF" onClick={() => setPdf(null)}
                className="flex size-7 shrink-0 items-center justify-center rounded-full bg-surface-2"><X className="size-4" /></button>
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
              {!photos.length && (
                <PhotoButton bare accept="application/pdf,.pdf" onFiles={pickPdf}
                  className="col-span-2 flex min-h-14 items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-border text-sm font-medium">
                  <FileText className="size-5 text-accent" />PDF of an online order
                </PhotoButton>
              )}
            </div>
          )}
          <p className="text-xs text-muted">
            {pdf ? "The first 4 pages are read. The PDF is kept with the receipt." : "Flat, well lit, whole width in frame. Long receipt? One photo per section, top to bottom (up to 4). Ordered online? Use the order's PDF receipt."}
            Every line is recorded with its price; food can go straight into inventory.
          </p>
          {error != null && <ErrorText error={error} />}
        </div>
      )}
      {reviewing && (save.error || error) ? <div className="mt-3"><ErrorText error={save.error ?? error} /></div> : null}
    </Sheet>
  );
}

function LineRow({ r, expenseCats, foodCats, locations, people, receiptSplit, update, changeCategory, updateFood, updateQuantity }: {
  r: Row; expenseCats: { id: string; label: string; emoji: string }[]; foodCats: Category[]; locations: Location[];
  people: Person[]; receiptSplit: number[];
  update: (p: Partial<Row>) => void; changeCategory: (category: string) => void; updateFood: (p: Partial<FoodRow>) => void; updateQuantity: (q: string) => void;
}) {
  const s = r.source;
  const lineQty = num(r.quantity) ?? s.quantity;
  // Saying yes to "same as X" takes on X's remembered size and inventory choice; no goes back to what the receipt said.
  const answer = (yes: boolean) => {
    update({ suggestionAccepted: yes });
    if (!r.food || !s.food) return;
    const pack = yes && s.suggestion!.packQuantity != null
      ? { perOne: s.suggestion!.packQuantity, unit: s.suggestion!.packUnit ?? "", from: "remembered" as const }
      : draftAmount(s);
    updateFood({
      add: yes ? !s.suggestion!.skipInventory && !r.food.already : s.food.add,
      ...(r.food.qtyTouched ? {} : { perOne: pack.perOne, unit: pack.unit, from: pack.from, qty: round3(lineQty * pack.perOne) }),
    });
  };
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
            <Select value={r.category} onChange={(e) => changeCategory(e.target.value)} className="min-h-8 w-auto py-0 text-xs" aria-label="Spending category">
              {expenseCats.map((c) => <option key={c.id} value={c.id}>{c.emoji} {c.label}</option>)}
            </Select>
            <span className="flex items-center gap-1">
              Qty
              <Input inputMode="decimal" value={r.quantity} onChange={(e) => updateQuantity(e.target.value)} className="min-h-8 w-14 px-2 text-xs" aria-label="Quantity bought" />
              <Input value={r.unit} onChange={(e) => update({ unit: e.target.value })} placeholder="unit" className="min-h-8 w-14 px-2 text-xs" aria-label="Unit bought" />
            </span>
            {s.discount != null && <Chip tone="accent">−{money(s.discount)} off</Chip>}
            {s.code && <span className="font-mono">#{s.code}</span>}
          </div>
          {s.item ? (
            <p className="mt-1.5 flex items-center gap-1 text-xs text-accent"><Link2 className="size-3.5" />{boughtBefore(s.item)}</p>
          ) : s.suggestion && (
            <div className="mt-1.5 flex flex-wrap items-center gap-2 rounded-xl bg-surface-2 px-2 py-1.5 text-xs">
              <span className="min-w-0 flex-1">Same as <b>{s.suggestion.name}</b>{s.suggestion.lastStore ? ` (${s.suggestion.lastStore}${s.suggestion.lastUnitPrice != null ? `, ${money(s.suggestion.lastUnitPrice)}` : ""})` : ""}?</span>
              <Button type="button" variant={r.suggestionAccepted === true ? "primary" : "secondary"} className="min-h-8 px-3 text-xs"
                onClick={() => answer(true)}>Yes</Button>
              <Button type="button" variant={r.suggestionAccepted === false ? "primary" : "secondary"} className="min-h-8 px-3 text-xs"
                onClick={() => answer(false)}>No</Button>
            </div>
          )}
          {r.food && (
            <div className="mt-2 rounded-xl bg-surface-2 p-2">
              <label className="flex items-center gap-2 text-xs font-medium">
                <input type="checkbox" checked={r.food.add} onChange={(e) => updateFood({ add: e.target.checked })} className="size-4 accent-[var(--color-accent)]" />
                Put away in inventory {r.food.product && <Chip tone="accent">Known item</Chip>}
              </label>
              {r.food.already && (
                <p className={cx("mt-1 text-[11px]", r.food.add ? "text-warn" : "text-muted")}>
                  Already put away {putAwayWhen(r.food.already.on)}: {formatAmount(r.food.already.quantity, r.food.already.unit)} in {r.food.already.location}.
                  {r.food.add ? " Ticked, it's added a second time." : " It gets this price."}
                </p>
              )}
              {!r.food.add && !r.food.already && (s.item?.skipInventory || (r.suggestionAccepted && s.suggestion?.skipInventory)) && (
                <p className="mt-1 text-[11px] text-muted">Left out last time. Spending is still tracked.</p>
              )}
              {r.food.add && (
                <div className="mt-2 grid grid-cols-2 gap-2">
                  <Input value={r.food.name} onChange={(e) => updateFood({ name: e.target.value })} className="col-span-2 min-h-9" aria-label="Inventory name" />
                  <div className="col-span-2 flex items-center gap-2 text-xs">
                    <span className="shrink-0 text-muted">Put away as</span>
                    <Input inputMode="decimal" value={r.food.qty} onChange={(e) => updateFood({ qty: e.target.value, qtyTouched: true })}
                      className="min-h-9 w-20" aria-label="Amount to put away" />
                    <Input value={r.food.unit} onChange={(e) => updateFood({ unit: e.target.value, qtyTouched: true })} placeholder="L, kg, pc…"
                      className="min-h-9 w-20" aria-label="Unit to put away" />
                  </div>
                  <p className="col-span-2 -mt-1 text-[11px] text-muted">{amountHint(r.food, lineQty)}</p>
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
          {people.length > 1 && <LineSplit r={r} people={people} receiptSplit={receiptSplit} onChange={(split) => update({ split })} />}
          {s.raw && s.raw !== r.name && <p className="mt-1 truncate font-mono text-[11px] text-muted">{s.raw}</p>}
        </>
      )}
    </li>
  );
}

/** "Split: Alex, Sam" for one line; tap to choose different people for just this line. */
function LineSplit({ r, people, receiptSplit, onChange }: {
  r: Row; people: Person[]; receiptSplit: number[]; onChange: (split: number[] | null) => void;
}) {
  const [open, setOpen] = useState(false);
  const who = r.split ?? receiptSplit;
  const names = people.filter((p) => who.includes(p.id)).map((p) => p.name).join(", ");
  return (
    <div className="mt-1.5 text-xs">
      <button type="button" onClick={() => setOpen(!open)} className={cx("text-left", r.split ? "text-accent" : "text-muted")}>
        Split: {who.length ? names : "not split"}{r.split ? " (this line)" : ""} ›
      </button>
      {open && (
        <div className="mt-1.5 flex flex-col gap-1.5">
          <PeopleChips small people={people} selected={who} onChange={(ids) => onChange(ids.length ? ids : null)} />
          {r.split && <button type="button" className="self-start text-muted underline" onClick={() => onChange(null)}>Same as the rest of the receipt</button>}
        </div>
      )}
    </div>
  );
}

/** The put-away amount the reader suggested, as "per one bought" so it can follow the quantity. */
function draftAmount(l: DraftLine): Pick<FoodRow, "qty" | "unit" | "qtyTouched" | "perOne" | "from"> {
  const f = l.food!;
  return { qty: round3(f.quantity), unit: f.unit ?? "", qtyTouched: false, perOne: l.quantity > 0 ? f.quantity / l.quantity : f.quantity, from: f.amountFrom };
}

function amountHint(f: FoodRow, lineQty: number) {
  const per = f.unit ? `${round3(f.perOne)} ${f.unit}` : null;
  if (f.qtyTouched) return "Remembered for next time as how much one of these is.";
  if (f.from === "remembered") return `One is ${per ?? round3(f.perOne)}, from last time. Change it if this one's different.`;
  if (f.from === "printed") return `Size from the receipt${lineQty !== 1 && per ? ` (${per} each)` : ""}.`;
  return "No size on the receipt. Enter how much it holds (e.g. 2 L) and it's remembered.";
}

function putAwayWhen(on: string | null) {
  if (!on || on === todayISO()) return "today";
  return parseISO(on).toLocaleDateString(undefined, { weekday: "long" });
}

const formatAmount = (q: number, unit: string | null) => `${round3(q)}${unit ? ` ${unit}` : ""}`;

function boughtBefore(i: ItemRef) {
  return `Bought before${i.lastUnitPrice != null ? `: last ${money(i.lastUnitPrice)}` : ""}${i.lastStore ? ` at ${i.lastStore}` : ""}`;
}
