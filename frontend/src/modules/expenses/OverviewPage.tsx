import { useState } from "react";
import { Link } from "react-router";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { Card, cx, Empty, Spinner } from "@/core/ui";
import { useExpenseCategories, useSummary, type Breakdown } from "./api";
import { DueList } from "./DueList";
import { monthLabel, money, shiftMonth } from "./format";
import { ReceiptList } from "./ReceiptsPage";

export function OverviewPage() {
  const [month, setMonth] = useState<string | undefined>(undefined);
  const summary = useSummary(month);
  const categories = useExpenseCategories().data ?? [];
  const emoji = new Map(categories.map((c) => [c.id, c.emoji]));
  const s = summary.data;
  if (!s) return <Spinner />;

  const thisMonth = !month;
  const diff = s.previousTotal != null ? s.total - s.previousTotal : null;
  const maxMonth = Math.max(...s.months.map((m) => m.total), 1);

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <div className="flex items-center justify-between">
          <button className="rounded-full p-2 text-muted hover:bg-surface-2" aria-label="Previous month" onClick={() => setMonth(shiftMonth(s.month, -1))}>
            <ChevronLeft className="size-5" />
          </button>
          <div className="text-center">
            <p className="text-sm text-muted">{monthLabel(s.month)}</p>
            <p className="text-3xl font-bold tabular-nums">{money(s.total)}</p>
            {diff != null && Math.abs(diff) >= 0.01 && (
              <p className={cx("text-xs", diff > 0 ? "text-warn" : "text-accent")}>
                {money(Math.abs(diff))} {diff > 0 ? "more" : "less"} than {monthLabel(shiftMonth(s.month, -1), true)}
              </p>
            )}
          </div>
          <button className="rounded-full p-2 text-muted hover:bg-surface-2 disabled:opacity-30" aria-label="Next month" disabled={thisMonth}
            onClick={() => setMonth(shiftMonth(s.month, 1))}>
            <ChevronRight className="size-5" />
          </button>
        </div>
        {/* Monthly totals; tap a bar to see that month. */}
        <div className="mt-4 flex h-20 items-end gap-2" aria-label="Spending by month">
          {s.months.map((m) => (
            <button key={m.month} onClick={() => setMonth(m.month)} className="flex h-full flex-1 flex-col items-center justify-end gap-1"
              aria-label={`${monthLabel(m.month)}: ${money(m.total)}`}>
              <span className={cx("w-full rounded-t-md", m.month === s.month ? "bg-accent" : "bg-border")}
                style={{ height: `${Math.max(4, (m.total / maxMonth) * 100)}%` }} />
              <span className={cx("text-[10px]", m.month === s.month ? "font-semibold text-text" : "text-muted")}>{monthLabel(m.month, true)}</span>
            </button>
          ))}
        </div>
      </Card>

      {s.total === 0 ? (
        <Empty title={thisMonth ? "Nothing spent yet this month" : "Nothing recorded this month"}>
          Scan a receipt, or add an expense with the + button.
        </Empty>
      ) : (
        <>
          <Card title="By category"><Bars rows={s.byCategory} total={s.total} icon={(k) => emoji.get(k) ?? (k === "tax" ? "🧾" : "📦")} /></Card>
          <Card title="By store"><Bars rows={s.byStore} total={s.total} /></Card>
        </>
      )}

      {thisMonth && <DueList />}

      <Card title="Recent" action={<Link to="/expenses/receipts" className="text-xs text-accent">All receipts →</Link>}>
        <ReceiptList limit={5} />
      </Card>
    </div>
  );
}

function Bars({ rows, total, icon }: { rows: Breakdown[]; total: number; icon?: (key: string) => string }) {
  const max = Math.max(...rows.map((r) => r.total), 0.01);
  return (
    <ul className="flex flex-col gap-2.5">
      {rows.map((r) => (
        <li key={r.key} className="text-sm">
          <div className="flex items-baseline gap-2">
            {icon && <span aria-hidden>{icon(r.key)}</span>}
            <span className="min-w-0 flex-1 truncate">{r.label}</span>
            <span className="text-xs text-muted">{Math.round((r.total / total) * 100)}%</span>
            <span className="w-20 text-right font-medium tabular-nums">{money(r.total)}</span>
          </div>
          <div className="mt-1 h-1.5 rounded-full bg-surface-2">
            <div className="h-full rounded-full bg-accent" style={{ width: `${Math.max(0, (r.total / max) * 100)}%` }} />
          </div>
        </li>
      ))}
    </ul>
  );
}
