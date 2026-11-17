import { useEffect, useState } from "react";
import { Trash2 } from "lucide-react";
import { ApiError } from "@/core/api";
import { todayISO } from "@/core/dates";
import { Button, ErrorText, Field, Input, Select, Sheet } from "@/core/ui";
import { useDeleteRecurring, useExpenseCategories, usePeople, useSaveRecurring, type Frequency, type Recurring } from "./api";
import { fromParts, noSplit, SplitEditor, toParts, type SplitValue } from "./Split";

const frequencies: { id: Frequency; label: string }[] = [
  { id: "monthly", label: "Every month" }, { id: "weekly", label: "Every week" },
  { id: "biweekly", label: "Every 2 weeks" }, { id: "yearly", label: "Every year" },
];

/**
 * A regular payment. With a fixed amount (rent) it's recorded automatically on the day; with "amount changes"
 * (electricity) it's an expected bill that shows as missing until that period's bill is uploaded.
 */
export function RecurringSheet({ open, onClose, recurring }: { open: boolean; onClose: () => void; recurring: Recurring | null }) {
  const categories = useExpenseCategories().data ?? [];
  const people = usePeople().data ?? [];
  const myId = people.find((p) => p.isMe)?.id ?? null;
  const save = useSaveRecurring();
  const del = useDeleteRecurring();
  const blank = { name: "", payee: "", category: "housing", varies: false, amount: "", frequency: "monthly" as Frequency, nextDate: todayISO(), active: true, note: "" };
  const [f, setF] = useState(blank);
  const [split, setSplit] = useState<SplitValue>(noSplit(myId));

  useEffect(() => {
    if (!open) return;
    save.reset(); del.reset();
    if (recurring) {
      setF({
        name: recurring.name, payee: recurring.payee ?? "", category: recurring.category, varies: recurring.expected,
        amount: recurring.amount?.toFixed(2) ?? "", frequency: recurring.frequency, nextDate: recurring.nextDate, active: recurring.active, note: recurring.note ?? "",
      });
      setSplit(fromParts(recurring.paidByPersonId ?? myId, recurring.split));
    } else { setF(blank); setSplit(noSplit(myId)); }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, recurring]);

  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });
  const amount = f.varies ? null : Number(f.amount.replace(/[$,\s]/g, "")) || null;
  const err = save.error instanceof ApiError ? save.error : undefined;
  const past = !f.varies && f.nextDate < todayISO();

  return (
    <Sheet open={open} onClose={onClose} title={recurring ? recurring.name : "Add a regular payment"}>
      <form className="grid grid-cols-2 gap-3" onSubmit={async (e) => {
        e.preventDefault();
        await save.mutateAsync({
          id: recurring?.id, name: f.name.trim(), payee: f.payee.trim() || null, category: f.category, amount, frequency: f.frequency,
          nextDate: f.nextDate, paidByPersonId: split.paidBy, split: toParts(split), active: f.active, note: f.note.trim() || null,
        });
        onClose();
      }}>
        <Field label="Name" className="col-span-2" error={err?.field("name")}><Input value={f.name} onChange={set("name")} required placeholder="Rent, Electricity…" /></Field>
        <Field label="Paid to (optional)" className="col-span-2" hint="Bills from this company are matched to it."><Input value={f.payee} onChange={set("payee")} placeholder="Landlord, Enmax…" /></Field>
        <div className="col-span-2 flex gap-2 text-sm">
          {[{ varies: false, label: "Same amount each time" }, { varies: true, label: "Amount changes (upload each bill)" }].map((o) => (
            <button key={String(o.varies)} type="button" onClick={() => setF({ ...f, varies: o.varies })}
              className={o.varies === f.varies ? "flex-1 rounded-xl border border-accent bg-accent-soft px-3 py-2 text-accent" : "flex-1 rounded-xl border border-border px-3 py-2 text-muted"}>
              {o.label}
            </button>
          ))}
        </div>
        {!f.varies && <Field label="Amount" error={err?.field("amount")}><Input inputMode="decimal" value={f.amount} onChange={set("amount")} required /></Field>}
        <Field label="How often" className={f.varies ? "col-span-2" : ""}>
          <Select value={f.frequency} onChange={set("frequency")}>{frequencies.map((x) => <option key={x.id} value={x.id}>{x.label}</option>)}</Select>
        </Field>
        <Field label={f.varies ? "Next bill expected" : "Next payment"} className="col-span-2"
          hint={f.varies ? "Shows as missing a few days after this if it hasn't been uploaded."
            : past ? "In the past: every payment since then is recorded as soon as you save." : "Recorded automatically on this day, then each time after."}>
          <Input type="date" value={f.nextDate} onChange={set("nextDate")} required />
        </Field>
        <Field label="Category" className="col-span-2">
          <Select value={f.category} onChange={set("category")}>{categories.map((c) => <option key={c.id} value={c.id}>{c.emoji} {c.label}</option>)}</Select>
        </Field>
        {people.length > 1 && <div className="col-span-2"><SplitEditor people={people} value={split} onChange={setSplit} total={amount} /></div>}
        {recurring && (
          <label className="col-span-2 flex items-center gap-2 text-sm">
            <input type="checkbox" checked={f.active} onChange={(e) => setF({ ...f, active: e.target.checked })} className="size-4 accent-[var(--color-accent)]" />
            Active (untick to pause)
          </label>
        )}
        {(save.error && !err?.errors) || del.error ? <div className="col-span-2"><ErrorText error={save.error ?? del.error} /></div> : null}
        <div className="col-span-2 flex gap-2">
          <Button variant="primary" className="flex-1" busy={save.isPending} disabled={!f.name.trim() || (!f.varies && !amount)}>Save</Button>
          {recurring && (
            <Button type="button" variant="ghost" busy={del.isPending} aria-label="Delete"
              onClick={async () => { await del.mutateAsync(recurring.id); onClose(); }}><Trash2 className="size-4" /></Button>
          )}
        </div>
        {recurring && <p className="col-span-2 text-xs text-muted">Deleting stops it. Payments already recorded stay.</p>}
      </form>
    </Sheet>
  );
}
