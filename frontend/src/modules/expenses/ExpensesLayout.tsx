import { useSearchParams } from "react-router";
import { FileText, PenLine, Plus, Receipt } from "lucide-react";
import { TabbedSection } from "@/core/layout";
import { Button, Sheet } from "@/core/ui";
import { BillSheet } from "./BillSheet";
import { ManualExpenseSheet } from "./ManualExpenseSheet";
import { ScanReceiptSheet } from "./ScanReceiptSheet";

const tabs = [
  { to: "/expenses", label: "Overview", end: true },
  { to: "/expenses/receipts", label: "History" },
  { to: "/expenses/bills", label: "Bills" },
  { to: "/expenses/owed", label: "Owed" },
  { to: "/expenses/items", label: "Items" },
];

type Adding = "choose" | "receipt" | "bill" | "expense";

/** `?add=receipt` (or bill, expense) opens that sheet straight away: Home's "Scan receipt" links here. */
export function ExpensesLayout() {
  const [params, setParams] = useSearchParams();
  const adding = params.get("add") as Adding | null;
  const open = (a: Adding | null) => setParams((p) => { if (a) p.set("add", a); else p.delete("add"); return p; }, { replace: true });
  return (
    <>
      <TabbedSection title="Money" tabs={tabs}
        action={<Button variant="primary" onClick={() => open("choose")}><Plus className="size-4" />Add</Button>} />
      <Sheet open={adding === "choose"} onClose={() => open(null)} title="Add">
        <div className="flex flex-col gap-2">
          <Choice icon={<Receipt className="size-5" />} title="Scan a receipt" text="Photos or an online order's PDF. Food can go into inventory too." onClick={() => open("receipt")} />
          <Choice icon={<FileText className="size-5" />} title="Upload a bill" text="Power, phone, internet… read from a PDF or photo." onClick={() => open("bill")} />
          <Choice icon={<PenLine className="size-5" />} title="Enter an expense" text="Something without a receipt." onClick={() => open("expense")} />
        </div>
      </Sheet>
      <ScanReceiptSheet open={adding === "receipt"} onClose={() => open(null)} onBack={() => open("choose")} />
      <BillSheet open={adding === "bill"} onClose={() => open(null)} />
      <ManualExpenseSheet open={adding === "expense"} onClose={() => open(null)} />
    </>
  );
}

function Choice({ icon, title, text, onClick }: { icon: React.ReactNode; title: string; text: string; onClick: () => void }) {
  return (
    <button onClick={onClick} className="press flex items-center gap-3 rounded-2xl border border-border p-3 text-left hover:bg-surface-2">
      <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-accent-soft text-accent">{icon}</span>
      <span className="min-w-0">
        <span className="block font-medium">{title}</span>
        <span className="block text-xs text-muted">{text}</span>
      </span>
    </button>
  );
}
