import { useEffect, useState } from "react";
import { ApiError } from "@/core/api";
import { todayISO } from "@/core/dates";
import { Button, ErrorText, Field, Input, Select, Sheet } from "@/core/ui";
import { useAddManual, useExpenseCategories } from "./api";

/** An expense with no receipt to scan: "$40 at the farmers' market". */
export function ManualExpenseSheet({ open, onClose }: { open: boolean; onClose: () => void }) {
  const categories = useExpenseCategories().data ?? [];
  const add = useAddManual();
  const blank = { description: "", amount: "", category: "groceries", store: "", purchasedAt: todayISO(), note: "" };
  const [f, setF] = useState(blank);
  useEffect(() => { if (open) { setF(blank); add.reset(); } }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });
  const amount = Number(f.amount.replace(/[$,\s]/g, ""));
  const err = add.error instanceof ApiError ? add.error : undefined;

  return (
    <Sheet open={open} onClose={onClose} title="Add an expense">
      <form className="grid grid-cols-2 gap-3" onSubmit={async (e) => {
        e.preventDefault();
        await add.mutateAsync({ description: f.description, amount, category: f.category, store: f.store || null, purchasedAt: f.purchasedAt, note: f.note || null });
        onClose();
      }}>
        <Field label="What for" className="col-span-2" error={err?.field("description")}>
          <Input value={f.description} onChange={set("description")} required placeholder="Farmers' market, haircut…" autoFocus />
        </Field>
        <Field label="Amount" error={err?.field("amount")}><Input inputMode="decimal" value={f.amount} onChange={set("amount")} required placeholder="0.00" /></Field>
        <Field label="Date"><Input type="date" value={f.purchasedAt} max={todayISO()} onChange={set("purchasedAt")} required /></Field>
        <Field label="Category" className="col-span-2">
          <Select value={f.category} onChange={set("category")}>{categories.map((c) => <option key={c.id} value={c.id}>{c.emoji} {c.label}</option>)}</Select>
        </Field>
        <Field label="Where (optional)" className="col-span-2"><Input value={f.store} onChange={set("store")} /></Field>
        <Field label="Note (optional)" className="col-span-2"><Input value={f.note} onChange={set("note")} /></Field>
        {add.error && !err?.errors && <div className="col-span-2"><ErrorText error={add.error} /></div>}
        <Button variant="primary" className="col-span-2" busy={add.isPending} disabled={!f.description.trim() || !(amount > 0)}>Add expense</Button>
      </form>
    </Sheet>
  );
}
