import { Link } from "react-router";
import { Camera, PenLine } from "lucide-react";
import { shortDate } from "@/core/dates";
import { Empty, Spinner } from "@/core/ui";
import { useReceipts, type ReceiptSummary } from "./api";
import { monthLabel, money } from "./format";

export function ReceiptsPage() {
  const receipts = useReceipts();
  if (!receipts.data) return <Spinner />;
  if (!receipts.data.length) return <Empty title="No receipts yet">Tap Scan to read your first receipt.</Empty>;

  // Grouped by month, newest first.
  const groups = new Map<string, ReceiptSummary[]>();
  for (const r of receipts.data) {
    const m = r.purchasedAt.slice(0, 7);
    groups.set(m, [...(groups.get(m) ?? []), r]);
  }
  return (
    <div className="flex flex-col gap-5">
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
              {r.source === "manual" ? <PenLine className="size-4" /> : <Camera className="size-4" />}
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate font-medium">{r.store ?? (r.source === "manual" ? "Expense" : "Receipt")}</p>
              <p className="text-xs text-muted">{shortDate(r.purchasedAt)} · {r.lineCount} {r.lineCount === 1 ? "line" : "lines"}</p>
            </div>
            <span className="font-medium tabular-nums">{money(r.total)}</span>
          </Link>
        </li>
      ))}
    </ul>
  );
}
