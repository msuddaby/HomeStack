import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { ArrowLeft, ImagePlus, Pencil, Trash2 } from "lucide-react";
import { shrinkImage } from "@/core/ai";
import { errorMessage } from "@/core/api";
import { shortDate, todayISO } from "@/core/dates";
import { Button, Card, ErrorText, Field, Input, PhotoButton, Sheet, Spinner } from "@/core/ui";
import { useAddReceiptPhotos, useDeleteReceipt, useExpenseCategories, useReceipt, useUpdateReceipt, type Receipt } from "./api";
import { money } from "./format";

export function ReceiptPage() {
  const id = Number(useParams().id);
  const receipt = useReceipt(id);
  const categories = useExpenseCategories().data ?? [];
  const emoji = new Map(categories.map((c) => [c.id, c.emoji]));
  const del = useDeleteReceipt();
  const addPhotos = useAddReceiptPhotos();
  const navigate = useNavigate();
  const [editing, setEditing] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [photoError, setPhotoError] = useState<string | null>(null);
  const [preparing, setPreparing] = useState(false);

  const r = receipt.data;
  if (receipt.error) return <ErrorText error={receipt.error} />;
  if (!r) return <Spinner />;

  const pickPhotos = async (files: File[]) => {
    setPhotoError(null); setPreparing(true);
    try {
      const blobs: Blob[] = [];
      for (const f of files.slice(0, 4 - r.images.length)) blobs.push(await shrinkImage(f));
      setPreparing(false);
      await addPhotos.mutateAsync({ id, photos: blobs });
    } catch (e) { setPhotoError(errorMessage(e)); } finally { setPreparing(false); }
  };

  return (
    <div className="flex flex-col gap-4">
      <Link to="/expenses/receipts" className="inline-flex items-center gap-1 text-sm text-muted"><ArrowLeft className="size-4" />Receipts</Link>
      <div className="flex items-start gap-3">
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-2xl font-bold">{r.store ?? (r.source === "manual" ? "Expense" : "Receipt")}</h1>
          <p className="text-sm text-muted">{shortDate(r.purchasedAt)}{r.note ? ` · ${r.note}` : ""}</p>
        </div>
        <p className="text-2xl font-bold tabular-nums">{money(r.total)}</p>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button onClick={() => setEditing(true)}><Pencil className="size-4" />Edit</Button>
        {r.images.length < 4 && (
          <PhotoButton multiple onFiles={pickPhotos} busy={preparing || addPhotos.isPending}>
            {!(preparing || addPhotos.isPending) && <ImagePlus className="size-4" />}
            {preparing ? "Preparing…" : addPhotos.isPending ? "Uploading…" : r.images.length ? "Add photo" : "Attach photo"}
          </PhotoButton>
        )}
        <Button variant="ghost" onClick={() => setConfirming(true)}><Trash2 className="size-4" />Delete</Button>
      </div>
      {photoError && <p className="text-sm text-danger">{photoError}</p>}

      <Card>
        <ul className="divide-y divide-border">
          {r.lines.map((l) => {
            const body = (
              <>
                <span aria-hidden>{emoji.get(l.category) ?? "📦"}</span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm">{l.name}{l.quantity !== 1 && <span className="text-muted"> ×{l.quantity}{l.unit ? ` ${l.unit}` : ""}</span>}</p>
                  {(l.code || l.discount) && (
                    <p className="truncate text-xs text-muted">
                      {l.code && <span className="font-mono">#{l.code}</span>}{l.code && l.discount ? " · " : ""}
                      {l.discount ? <span className="text-accent">−{money(l.discount)} off</span> : null}
                    </p>
                  )}
                </div>
                <span className="text-sm tabular-nums">{money(l.amount)}</span>
              </>
            );
            return (
              <li key={l.id}>
                {l.itemId
                  ? <Link to={`/expenses/items/${l.itemId}`} className="flex items-center gap-2 py-2 hover:bg-surface-2">{body}</Link>
                  : <div className="flex items-center gap-2 py-2">{body}</div>}
              </li>
            );
          })}
        </ul>
        <dl className="mt-2 flex flex-col gap-1 border-t border-border pt-2 text-sm">
          {r.subtotal != null && <Row label="Subtotal" value={money(r.subtotal)} />}
          {r.tax != null && <Row label="Tax" value={money(r.tax)} />}
          <Row label="Total" value={money(r.total)} strong />
        </dl>
      </Card>

      {r.images.length > 0 && (
        <div className="flex flex-col gap-2">
          {r.images.map((src, i) => (
            <a key={src} href={src} target="_blank" rel="noreferrer">
              <img src={src} alt={`Receipt photo ${i + 1}`} className="w-full rounded-2xl border border-border" loading="lazy" />
            </a>
          ))}
        </div>
      )}

      <EditSheet receipt={r} open={editing} onClose={() => setEditing(false)} />
      <Sheet open={confirming} onClose={() => setConfirming(false)} title="Delete this receipt?">
        <p className="text-sm">
          It comes off your spending, with its photos. Anything it put into inventory stays there.
        </p>
        {del.error && <div className="mt-3"><ErrorText error={del.error} /></div>}
        <div className="mt-4 flex gap-2">
          <Button className="flex-1" onClick={() => setConfirming(false)}>Keep it</Button>
          <Button variant="danger" className="flex-1" busy={del.isPending}
            onClick={async () => { await del.mutateAsync(id); navigate("/expenses/receipts", { replace: true }); }}>Delete</Button>
        </div>
      </Sheet>
    </div>
  );
}

function Row({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={strong ? "flex justify-between font-semibold" : "flex justify-between text-muted"}>
      <dt>{label}</dt><dd className="tabular-nums">{value}</dd>
    </div>
  );
}

function EditSheet({ receipt, open, onClose }: { receipt: Receipt; open: boolean; onClose: () => void }) {
  const update = useUpdateReceipt();
  const [f, setF] = useState({ store: receipt.store ?? "", purchasedAt: receipt.purchasedAt, note: receipt.note ?? "" });
  return (
    <Sheet open={open} onClose={onClose} title="Edit receipt">
      <form className="flex flex-col gap-3" onSubmit={async (e) => {
        e.preventDefault();
        await update.mutateAsync({ id: receipt.id, store: f.store || null, purchasedAt: f.purchasedAt, note: f.note || null });
        onClose();
      }}>
        <Field label="Store"><Input value={f.store} onChange={(e) => setF({ ...f, store: e.target.value })} /></Field>
        <Field label="Date"><Input type="date" value={f.purchasedAt} max={todayISO()} onChange={(e) => setF({ ...f, purchasedAt: e.target.value })} required /></Field>
        <Field label="Note"><Input value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} /></Field>
        {update.error && <ErrorText error={update.error} />}
        <Button variant="primary" busy={update.isPending}>Save</Button>
      </form>
    </Sheet>
  );
}
