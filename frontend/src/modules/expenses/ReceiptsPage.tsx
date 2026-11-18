import { Link, useSearchParams } from "react-router";
import { Camera, FileText, PenLine, Repeat } from "lucide-react";
import { shortDate } from "@/core/dates";
import { cx, Empty, Spinner } from "@/core/ui";
import { useReceipts, type ReceiptSummary } from "./api";
import { monthLabel, money } from "./format";

type Kind = "all" | "receipts" | "bills" | "other";

const KINDS: { id: Kind; label: string; empty: string }[] = [
  { id: "all", label: "All", empty: "Tap Add to scan a receipt, upload a bill or enter an expense." },
  { id: "receipts", label: "Receipts", empty: "Tap Add, then Scan a receipt." },
  { id: "bills", label: "Bills", empty: "Tap Add, then Upload a bill." },
  { id: "other", label: "Other", empty: "Expenses entered by hand and regular payments (like rent) show here." },
];

const kindOf = (r: ReceiptSummary): Kind => r.source === "scan" ? "receipts" : r.source === "bill" ? "bills" : "other";

/** Everything spent: scanned receipts, bills, expenses entered by hand and regular payments, by month. */
export function ReceiptsPage() {
  const receipts = useReceipts();
  const [params, setParams] = useSearchParams();
  const kind = (KINDS.find((k) => k.id === params.get("type"))?.id ?? "all");
  const setKind = (k: Kind) => setParams((p) => { if (k === "all") p.delete("type"); else p.set("type", k); return p; }, { replace: true });
  if (!receipts.data) return <Spinner />;

  const counts = new Map<Kind, number>([["all", receipts.data.length]]);
  for (const r of receipts.data) counts.set(kindOf(r), (counts.get(kindOf(r)) ?? 0) + 1);
  const shown = kind === "all" ? receipts.data : receipts.data.filter((r) => kindOf(r) === kind);

  // Grouped by month, newest first.
  const groups = new Map<string, ReceiptSummary[]>();
  for (const r of shown) {
    const m = r.purchasedAt.slice(0, 7);
    groups.set(m, [...(groups.get(m) ?? []), r]);
  }
  return (
    <div className="flex flex-col gap-5">
      <div className="-mx-4 -mb-2 flex gap-2 overflow-x-auto px-4 pb-1">
        {KINDS.map((k) => (
          <button key={k.id} onClick={() => setKind(k.id)}
            className={cx("press flex min-h-10 shrink-0 items-center gap-1.5 rounded-full border px-4 text-sm font-medium transition-colors duration-200",
              kind === k.id ? "border-accent bg-accent text-accent-text" : "border-border bg-surface text-text")}>
            {k.label}<span className={cx("text-xs", kind === k.id ? "opacity-80" : "text-muted")}>{counts.get(k.id) ?? 0}</span>
          </button>
        ))}
      </div>
      {!shown.length && <Empty title="Nothing here yet">{KINDS.find((k) => k.id === kind)!.empty}</Empty>}
      {[...groups].map(([m, rs]) => (
        <section key={m}>
          <h2 className="mb-1 flex justify-between text-xs font-semibold uppercase tracking-wide text-muted">
            <span>{monthLabel(m)}</span><span className="tabular-nums">{money(rs.reduce((s, r) => s + r.total, 0))}</span>
          </h2>
          <Rows receipts={rs} />
        </section>
      ))}
    </div>
  );
}

/** The most recent receipts, for the overview. */
export function ReceiptList({ limit }: { limit: number }) {
  const receipts = useReceipts();
  if (!receipts.data) return <Spinner />;
  if (!receipts.data.length) return <p className="text-sm text-muted">Nothing recorded yet.</p>;
  return <Rows receipts={receipts.data.slice(0, limit)} />;
}

function Rows({ receipts }: { receipts: ReceiptSummary[] }) {
  return (
    <ul className="divide-y divide-border">
      {receipts.map((r) => (
        <li key={r.id}>
          <Link to={`/expenses/receipts/${r.id}`} className="flex items-center gap-3 py-2.5 hover:bg-surface-2 sm:px-2">
            <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-surface-2 text-muted">
              {r.source === "manual" ? <PenLine className="size-4" /> : r.source === "bill" ? <FileText className="size-4" />
                : r.source === "recurring" ? <Repeat className="size-4" /> : <Camera className="size-4" />}
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate font-medium">{r.store ?? (r.source === "manual" ? "Expense" : r.source === "bill" ? "Bill" : "Receipt")}</p>
              <p className="text-xs text-muted">
                {shortDate(r.purchasedAt)}
                {r.source === "scan" && ` · ${r.lineCount} ${r.lineCount === 1 ? "line" : "lines"}`}
                {r.source === "bill" && (r.billPaidOn ? " · paid" : r.dueDate ? ` · due ${shortDate(r.dueDate)}` : " · unpaid")}
                {r.outstanding > 0 && ` · ${money(r.outstanding)} owed`}
              </p>
            </div>
            <span className="font-medium tabular-nums">{money(r.total)}</span>
          </Link>
        </li>
      ))}
    </ul>
  );
}
