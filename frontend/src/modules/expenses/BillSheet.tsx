import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router";
import { Camera, Check, FileText, Sparkles, X } from "lucide-react";
import { shrinkImage, useAiSettings } from "@/core/ai";
import { errorMessage } from "@/core/api";
import { shortDate, todayISO } from "@/core/dates";
import { useToast } from "@/core/toast";
import { Button, ErrorText, Field, Input, PhotoButton, Select, Sheet, Spinner } from "@/core/ui";
import { readBill, useAttachDocument, useExpenseCategories, usePeople, useRecurring, useSaveBill, type BillDraft } from "./api";
import { money } from "./format";
import { fromParts, noSplit, SplitEditor, toParts, type SplitValue } from "./Split";

/** A picked file: a PDF kept as-is, or a photo already shrunk. */
interface Picked { blob: Blob; name: string; pdf: boolean; preview: string | null }

const isPdf = (f: File) => f.type === "application/pdf" || f.name.toLowerCase().endsWith(".pdf");
const num = (s: string) => { const n = Number(s.replace(/[$,\s]/g, "")); return s.trim() !== "" && Number.isFinite(n) ? n : null; };

/** Upload a bill (PDF or photos) → AI reads it → review company, dates, amount and split → save, keeping the original. */
export function BillSheet({ open, onClose }: { open: boolean; onClose: () => void }) {
  const ai = useAiSettings();
  const categories = useExpenseCategories().data ?? [];
  const people = usePeople().data ?? [];
  const myId = people.find((p) => p.isMe)?.id ?? null;
  const expected = (useRecurring().data ?? []).filter((r) => r.active);
  const save = useSaveBill();
  const attach = useAttachDocument();
  const toast = useToast();
  const navigate = useNavigate();

  const [files, setFiles] = useState<Picked[]>([]);
  const [preparing, setPreparing] = useState(false);
  const [reading, setReading] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [draft, setDraft] = useState<BillDraft | null>(null);
  const blankForm = { payee: "", statementDate: todayISO(), dueDate: "", amount: "", category: "utilities", description: "", note: "", recurringId: "", paid: false, paidOn: todayISO() };
  const [f, setF] = useState(blankForm);
  const [split, setSplit] = useState<SplitValue>(noSplit(myId));

  useEffect(() => {
    if (!open) return;
    setFiles([]); setDraft(null); setError(null); setF(blankForm); setSplit(noSplit(myId)); save.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);
  useEffect(() => () => files.forEach((p) => p.preview && URL.revokeObjectURL(p.preview)), [files]);

  // Copy or shrink straight away, while iOS still lets the page read the picked file.
  async function pick(picked: File[]) {
    setError(null); setPreparing(true);
    try {
      const pdf = picked.find(isPdf);
      if (pdf) {
        const blob = new Blob([await pdf.arrayBuffer()], { type: "application/pdf" });
        setFiles([{ blob, name: pdf.name || "bill.pdf", pdf: true, preview: null }]);
        return;
      }
      const added: Picked[] = [];
      for (const file of picked.slice(0, 4 - files.filter((x) => !x.pdf).length)) {
        const blob = await shrinkImage(file, 2000, 0.88);
        added.push({ blob, name: "bill.jpg", pdf: false, preview: URL.createObjectURL(blob) });
      }
      setFiles((xs) => [...xs.filter((x) => !x.pdf), ...added].slice(0, 4));
    } catch (e) { setError(e); } finally { setPreparing(false); }
  }

  async function read() {
    setReading(true); setError(null);
    try {
      const form = new FormData();
      files.forEach((x) => form.append("files", x.blob, x.name));
      const d = await readBill(form);
      const period = d.periodStart && d.periodEnd ? ` (${shortDate(d.periodStart)} – ${shortDate(d.periodEnd)})` : "";
      setDraft(d);
      setF({
        ...blankForm, payee: d.payee ?? "", statementDate: d.statementDate ?? todayISO(), dueDate: d.dueDate ?? "",
        amount: d.amount?.toFixed(2) ?? "", category: d.category, description: d.payee ? `${d.payee}${period}` : "",
        note: d.note ?? "", recurringId: d.recurring ? String(d.recurring.id) : "",
      });
      // An expected bill brings its usual split (electricity between roommates).
      if (d.recurring) setSplit(fromParts(d.recurring.paidByPersonId ?? myId, d.recurring.split));
    } catch (e) { setError(e); } finally { setReading(false); }
  }

  const chooseRecurring = (id: string) => {
    setF({ ...f, recurringId: id });
    const r = expected.find((x) => String(x.id) === id);
    if (r) setSplit(fromParts(r.paidByPersonId ?? myId, r.split));
  };

  const amount = num(f.amount);
  async function saveAll() {
    if (amount == null || !f.payee.trim()) return;
    const saved = await save.mutateAsync({
      payee: f.payee.trim(), statementDate: f.statementDate, dueDate: f.dueDate || null, amount, category: f.category,
      description: f.description.trim() || null, note: f.note.trim() || null, paidByPersonId: split.paidBy, split: toParts(split),
      recurringId: f.recurringId ? Number(f.recurringId) : null, paidOn: f.paid ? f.paidOn : null,
    });
    let fileNote = "";
    try { for (const x of files) await attach.mutateAsync({ id: saved.id, file: x.blob, name: x.name }); }
    catch (e) { fileNote = ` The file wasn't attached: ${errorMessage(e)}`; }
    toast({
      message: `Saved ${saved.store ?? "bill"}, ${money(saved.total)}.${fileNote}`, tone: fileNote ? "danger" : undefined,
      action: { label: "View", onClick: () => navigate(`/expenses/receipts/${saved.id}`) },
    });
    onClose();
  }

  const reviewing = !!draft;
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });
  const footer = !ai.data?.configured ? undefined : reviewing ? (
    <div className="flex gap-2">
      <Button className="shrink-0" onClick={() => setDraft(null)}>Back</Button>
      <Button className="flex-1" variant="primary" disabled={amount == null || !f.payee.trim() || !f.statementDate} busy={save.isPending || attach.isPending} onClick={saveAll}>
        <Check className="size-4" />Save {amount != null ? money(amount) : "bill"}
      </Button>
    </div>
  ) : (
    <Button className="w-full" variant="primary" busy={reading} disabled={!files.length || preparing} onClick={read}>
      <Sparkles className="size-4" />{reading ? "Reading…" : "Read bill"}
    </Button>
  );

  return (
    <Sheet open={open} onClose={onClose} title={reviewing ? (f.payee || "Check the bill") : "Upload a bill"} footer={footer}>
      {!ai.data ? <Spinner /> : !ai.data.configured ? (
        <div className="flex flex-col items-start gap-3 text-sm">
          <p>Reading bills uses an AI service. Pick one in Settings first.</p>
          <Link to="/settings" onClick={onClose}><Button variant="primary"><Sparkles className="size-4" />Set up AI</Button></Link>
        </div>
      ) : reading ? (
        <div className="flex flex-col items-center gap-3 py-10 text-center text-sm text-muted"><Spinner /><p>Reading your bill… usually 10–30 seconds.</p></div>
      ) : reviewing ? (
        <div className="flex flex-col gap-3">
          {draft.duplicateOf != null && (
            <p className="rounded-xl bg-warn-soft p-3 text-sm text-warn">
              Looks like this bill is already saved. <Link to={`/expenses/receipts/${draft.duplicateOf}`} onClick={onClose} className="underline">See it</Link>.
            </p>
          )}
          {draft.note && <p className="rounded-xl bg-surface-2 p-3 text-sm">{draft.note}</p>}
          <div className="grid grid-cols-2 gap-3">
            <Field label="Company" className="col-span-2"><Input value={f.payee} onChange={set("payee")} required /></Field>
            <Field label="Amount due"><Input inputMode="decimal" value={f.amount} onChange={set("amount")} required /></Field>
            <Field label="Due"><Input type="date" value={f.dueDate} onChange={set("dueDate")} /></Field>
            <Field label="Bill date"><Input type="date" value={f.statementDate} onChange={set("statementDate")} required /></Field>
            <Field label="Category">
              <Select value={f.category} onChange={set("category")}>{categories.map((c) => <option key={c.id} value={c.id}>{c.emoji} {c.label}</option>)}</Select>
            </Field>
            <Field label="Description" className="col-span-2"><Input value={f.description} onChange={set("description")} /></Field>
            {expected.length > 0 && (
              <Field label="Regular bill" className="col-span-2" hint="Counts as this period's bill, so it's no longer shown as missing.">
                <Select value={f.recurringId} onChange={(e) => chooseRecurring(e.target.value)}>
                  <option value="">None</option>
                  {expected.map((r) => <option key={r.id} value={r.id}>{r.name}{r.payee && r.payee !== r.name ? ` (${r.payee})` : ""}</option>)}
                </Select>
              </Field>
            )}
          </div>
          {people.length > 1 && <SplitEditor people={people} value={split} onChange={setSplit} total={amount} />}
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={f.paid} onChange={(e) => setF({ ...f, paid: e.target.checked })} className="size-4 accent-[var(--color-accent)]" />
            Already paid to {f.payee || "the company"}
            {f.paid && <Input type="date" value={f.paidOn} onChange={set("paidOn")} className="ml-auto min-h-9 w-40" />}
          </label>
          <p className="text-xs text-muted">Check the amount and dates. AI can misread bills. The original file is kept with it.</p>
          {(save.error || error) ? <ErrorText error={save.error ?? error} /> : null}
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {files.length > 0 && (
            <div className="flex gap-2 overflow-x-auto pb-1">
              {files.map((x, i) => (
                <div key={i} className="relative shrink-0">
                  {x.preview
                    ? <img src={x.preview} alt={`Bill page ${i + 1}`} className="h-36 w-24 rounded-xl border border-border object-cover" />
                    : <div className="flex h-36 w-28 flex-col items-center justify-center gap-1 rounded-xl border border-border p-2 text-center text-xs"><FileText className="size-8 text-accent" /><span className="line-clamp-2 break-all">{x.name}</span></div>}
                  <button type="button" aria-label="Remove" onClick={() => setFiles((xs) => xs.filter((y) => y !== x))}
                    className="absolute -right-1.5 -top-1.5 flex size-6 items-center justify-center rounded-full bg-text text-surface"><X className="size-3.5" /></button>
                </div>
              ))}
            </div>
          )}
          {preparing ? (
            <div className="flex min-h-28 items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-border text-sm text-muted"><Spinner />Preparing…</div>
          ) : !files.some((x) => x.pdf) && files.length < 4 && (
            <div className="grid grid-cols-2 gap-2">
              <PhotoButton bare camera onFiles={pick}
                className="flex min-h-28 flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-border text-sm font-medium">
                <Camera className="size-7 text-accent" />{files.length ? "Another page" : "Take photo"}
              </PhotoButton>
              <PhotoButton bare multiple accept="application/pdf,image/*" onFiles={pick}
                className="flex min-h-28 flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-border text-sm font-medium">
                <FileText className="size-7 text-accent" />PDF or photos
              </PhotoButton>
            </div>
          )}
          <p className="text-xs text-muted">A PDF from the company's website works best (first 4 pages are read). Or photograph each page.</p>
          {error != null && <ErrorText error={error} />}
        </div>
      )}
    </Sheet>
  );
}
