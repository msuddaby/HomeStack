import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router";
import { Camera, Check, ImagePlus, Sparkles, X } from "lucide-react";
import { shrinkImage, useAiSettings } from "@/core/ai";
import { errorMessage } from "@/core/api";
import { shortDate, todayISO } from "@/core/dates";
import { Button, Chip, cx, ErrorText, Input, PhotoButton, Select, Sheet, Spinner } from "@/core/ui";
import { readReceipt, readText, useAddItems, useCategories, useLocations, type AddItem, type Category, type Location, type ReadItem, type ReadResult } from "./api";
import { suggestExpiry } from "./logic";

export type ReadMode = "receipt" | "text";

interface Row {
  key: number; include: boolean; name: string; category: string; locationId: number; quantity: string; unit: string;
  expiresAt: string; expiryTouched: boolean; source: ReadItem;
}

/** Receipt photo or typed list → AI → editable review list → add all. */
export function ReadSheet({ open, mode, onBack, onDone }: { open: boolean; mode: ReadMode; onBack: () => void; onDone: () => void }) {
  const ai = useAiSettings();
  const categories = useCategories().data ?? [];
  const locations = useLocations().data ?? [];
  const addItems = useAddItems();

  // Already shrunk: see addPhotos.
  const [photos, setPhotos] = useState<{ blob: Blob; url: string }[]>([]);
  const [preparing, setPreparing] = useState(false);
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [result, setResult] = useState<ReadResult | null>(null);
  const [rows, setRows] = useState<Row[]>([]);
  const [purchasedAt, setPurchasedAt] = useState(todayISO());

  useEffect(() => {
    if (!open) return;
    setPhotos([]); setText(""); setResult(null); setRows([]); setError(null); setPurchasedAt(todayISO()); addItems.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, mode]);
  useEffect(() => () => photos.forEach((p) => URL.revokeObjectURL(p.url)), [photos]);

  const locFor = (kind: string): Location | undefined => locations.find((l) => l.kind === kind) ?? locations[0];
  const catById = useMemo(() => new Map(categories.map((c) => [c.id, c])), [categories]);

  function suggested(r: Pick<Row, "category" | "locationId" | "source">, bought: string) {
    const loc = locations.find((l) => l.id === r.locationId);
    if (!loc) return "";
    return suggestExpiry(catById.get(r.category), loc.kind, bought, null, r.source.product ?? undefined) ?? "";
  }

  // Shrink right away, while the picker's files are still readable (iOS can discard them once the input is
  // reset), and keep the small copies until "Read receipt".
  async function addPhotos(files: File[]) {
    setError(null);
    setPreparing(true);
    try {
      const room = 4 - photos.length;
      const added: { blob: Blob; url: string }[] = [];
      for (const file of files.slice(0, room)) {
        const blob = await shrinkImage(file);
        added.push({ blob, url: URL.createObjectURL(blob) });
      }
      setPhotos((p) => [...p, ...added].slice(0, 4));
    } catch (e) {
      setError(e);
    } finally {
      setPreparing(false);
    }
  }

  async function read() {
    setBusy(true); setError(null);
    try {
      let r: ReadResult;
      if (mode === "receipt") {
        const form = new FormData();
        for (const p of photos) form.append("images", p.blob, "receipt.jpg");
        r = await readReceipt(form);
      } else {
        r = await readText(text);
      }
      const bought = r.purchasedAt ?? todayISO();
      setPurchasedAt(bought);
      setResult(r);
      setRows(r.items.map((it, i) => {
        const base = { category: it.category, locationId: locFor(it.location)?.id ?? 0, source: it };
        return { key: i, include: true, name: it.name, quantity: String(it.quantity), unit: it.unit ?? "", expiryTouched: false,
          expiresAt: suggested(base, bought), ...base };
      }));
    } catch (e) { setError(e); }
    finally { setBusy(false); }
  }

  function update(key: number, patch: Partial<Row>) {
    setRows((rs) => rs.map((r) => {
      if (r.key !== key) return r;
      const next = { ...r, ...patch };
      if (!next.expiryTouched && ("category" in patch || "locationId" in patch)) next.expiresAt = suggested(next, purchasedAt);
      return next;
    }));
  }

  function changeDate(d: string) {
    setPurchasedAt(d);
    setRows((rs) => rs.map((r) => (r.expiryTouched ? r : { ...r, expiresAt: suggested(r, d || todayISO()) })));
  }

  const chosen = rows.filter((r) => r.include && r.name.trim() && r.locationId);

  async function addAll() {
    const items: AddItem[] = chosen.map((r) => {
      const p = r.source.product;
      const same = p && p.name.toLowerCase() === r.name.trim().toLowerCase() && p.category === r.category;
      return {
        ...(same ? { productId: p.id } : { product: { name: r.name.trim(), category: r.category, brand: r.source.brand, defaultUnit: r.unit || null } }),
        locationId: r.locationId,
        quantity: Number(r.quantity) || 1,
        unit: r.unit || null,
        purchasedAt: purchasedAt || null,
        expiresAt: r.expiresAt || null,
        price: r.source.price,
        notes: result?.store ? `From ${result.store}` : null,
      };
    });
    await addItems.mutateAsync(items);
    onDone();
  }

  const reviewing = !!result;
  const title = reviewing ? (result.store ?? "Check these items") : mode === "receipt" ? "Scan a receipt" : "Type a list";

  const footer = !ai.data?.configured ? undefined : reviewing ? (
    <div className="flex gap-2">
      <Button className="shrink-0" onClick={() => { setResult(null); setRows([]); }}>Back</Button>
      <Button className="flex-1" variant="primary" disabled={!chosen.length} busy={addItems.isPending} onClick={addAll}>
        <Check className="size-4" />Add {chosen.length} {chosen.length === 1 ? "item" : "items"}
      </Button>
    </div>
  ) : (
    <div className="flex gap-2">
      <Button className="shrink-0" onClick={onBack}>Back</Button>
      <Button className="flex-1" variant="primary" busy={busy} disabled={mode === "receipt" ? !photos.length : !text.trim()} onClick={read}>
        <Sparkles className="size-4" />{busy ? "Reading…" : mode === "receipt" ? "Read receipt" : "Read list"}
      </Button>
    </div>
  );

  return (
    <Sheet open={open} onClose={onDone} title={title} footer={footer}>
      {!ai.data ? <Spinner /> : !ai.data.configured ? (
        <div className="flex flex-col items-start gap-3 text-sm">
          <p>Reading receipts and lists uses an AI service. Pick one (DeepSeek, Claude, a local Ollama model…) in Settings first.</p>
          <Link to="/settings" onClick={onDone}><Button variant="primary"><Sparkles className="size-4" />Set up AI</Button></Link>
          <Button variant="ghost" onClick={onBack}>Back</Button>
        </div>
      ) : busy ? (
        <div className="flex flex-col items-center gap-3 py-10 text-center text-sm text-muted">
          <Spinner />
          <p>Reading {mode === "receipt" ? "your receipt" : "your list"}… usually 10–30 seconds.</p>
        </div>
      ) : reviewing ? (
        <Review rows={rows} result={result} purchasedAt={purchasedAt} onDate={changeDate} update={update}
          categories={categories} locations={locations} catById={catById} />
      ) : mode === "receipt" ? (
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
              <PhotoButton bare camera onFiles={addPhotos}
                className="flex min-h-28 flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-border text-sm font-medium">
                <Camera className="size-7 text-accent" />{photos.length ? "Another photo" : "Take photo"}
              </PhotoButton>
              <PhotoButton bare multiple onFiles={addPhotos}
                className="flex min-h-28 flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-border text-sm font-medium">
                <ImagePlus className="size-7 text-accent" />From photos
              </PhotoButton>
            </div>
          )}
          <p className="text-xs text-muted">Flat, well lit, whole width in frame. Long receipt? Take one photo per section, top to bottom (up to 4).</p>
          {error != null && <ErrorText error={error} />}
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          <textarea value={text} onChange={(e) => setText(e.target.value)} rows={6} autoFocus maxLength={4000}
            placeholder={"2 lbs chicken thighs, a dozen eggs, milk, 3 cans diced tomatoes, frozen peas"}
            className="w-full rounded-xl border border-border bg-surface p-3 text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent/25" />
          <p className="text-xs text-muted">Write it however you like, or use your keyboard's microphone to say it.</p>
          {error != null && <p className="text-sm text-danger">{errorMessage(error)}</p>}
        </div>
      )}
    </Sheet>
  );
}

function Review({ rows, result, purchasedAt, onDate, update, categories, locations, catById }: {
  rows: Row[]; result: ReadResult; purchasedAt: string; onDate: (d: string) => void; update: (key: number, patch: Partial<Row>) => void;
  categories: Category[]; locations: Location[]; catById: Map<string, Category>;
}) {
  return (
    <div className="flex flex-col gap-3">
      <label className="flex items-center justify-between gap-3 text-sm">
        <span className="text-muted">Bought</span>
        <Input type="date" className="max-w-44" value={purchasedAt} max={todayISO()} onChange={(e) => onDate(e.target.value)} />
      </label>
      {result.note && <p className="rounded-xl bg-warn-soft p-3 text-sm text-warn">{result.note}</p>}
      {!rows.length && <p className="text-sm text-muted">No food items found. Try a clearer photo, or type the list instead.</p>}

      <ul className="flex flex-col gap-2">
        {rows.map((r) => (
          <li key={r.key} className={cx("rounded-2xl border border-border p-3", !r.include && "opacity-50")}>
            <div className="flex items-center gap-2">
              <input type="checkbox" checked={r.include} onChange={(e) => update(r.key, { include: e.target.checked })}
                aria-label={`Include ${r.name}`} className="size-5 shrink-0 accent-[var(--color-accent)]" />
              <span className="text-lg" aria-hidden>{catById.get(r.category)?.emoji}</span>
              <Input value={r.name} onChange={(e) => update(r.key, { name: e.target.value })} className="min-h-10 min-w-0 flex-1 font-medium" aria-label="Name" />
            </div>
            {r.include && (
              <div className="mt-2 grid grid-cols-[4rem_4.5rem_1fr] gap-2">
                <Input inputMode="decimal" value={r.quantity} onChange={(e) => update(r.key, { quantity: e.target.value })} className="min-h-10" aria-label="Quantity" />
                <Input value={r.unit} placeholder="unit" onChange={(e) => update(r.key, { unit: e.target.value })} className="min-h-10" aria-label="Unit" />
                <Select value={r.locationId} onChange={(e) => update(r.key, { locationId: Number(e.target.value) })} className="min-h-10" aria-label="Where">
                  {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
                </Select>
                <Select value={r.category} onChange={(e) => update(r.key, { category: e.target.value })} className="col-span-2 min-h-10" aria-label="Category">
                  {categories.map((c) => <option key={c.id} value={c.id}>{c.emoji} {c.label}</option>)}
                </Select>
                <Input type="date" value={r.expiresAt} onChange={(e) => update(r.key, { expiresAt: e.target.value, expiryTouched: true })}
                  className="min-h-10" aria-label="Expires" title={r.expiresAt ? `Expires ${shortDate(r.expiresAt)}` : "No expiry"} />
              </div>
            )}
            <div className="mt-1.5 flex flex-wrap items-center gap-1.5 text-xs text-muted">
              {r.source.product && <Chip tone="accent">Known item</Chip>}
              {r.source.rawText && r.source.rawText !== r.name && <span className="font-mono">{r.source.rawText}</span>}
              {r.source.price != null && <span>${r.source.price.toFixed(2)}</span>}
            </div>
          </li>
        ))}
      </ul>

      {result.skipped.length > 0 && (
        <details className="text-xs text-muted">
          <summary className="cursor-pointer">Skipped {result.skipped.length} non-food {result.skipped.length === 1 ? "line" : "lines"}</summary>
          <ul className="mt-1 list-disc pl-5 font-mono">{result.skipped.map((s, i) => <li key={i}>{s}</li>)}</ul>
        </details>
      )}
      <p className="text-xs text-muted">Check names, places and dates. AI can misread receipts.</p>
    </div>
  );
}
