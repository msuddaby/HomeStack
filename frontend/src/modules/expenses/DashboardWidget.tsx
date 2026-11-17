import { Link } from "react-router";
import { AlertTriangle, CalendarClock, Wallet } from "lucide-react";
import { shortDate, todayISO } from "@/core/dates";
import { Card, cx } from "@/core/ui";
import { useBalances, useSummary, useUpcoming } from "./api";
import { DueList } from "./DueList";
import { monthLabel, money } from "./format";

export function ExpensesDashboardWidget() {
  const summary = useSummary();
  const s = summary.data;
  const balances = useBalances().data;
  const me = balances?.myPersonId;
  const iOwe = (balances?.balances ?? []).filter((b) => b.personId === me).reduce((t, b) => t + b.outstanding, 0);
  const owedToMe = (balances?.balances ?? []).filter((b) => b.owedToPersonId === me).reduce((t, b) => t + b.outstanding, 0);
  return (
    <>
      {s && (
        <Card title={<span className="flex items-center gap-2"><Wallet className="size-4 text-accent" />Spending</span>}
          action={<Link to="/expenses" className="text-xs text-accent">Details →</Link>}>
          <p className="text-sm text-muted">{monthLabel(s.month)}</p>
          <p className="text-2xl font-bold tabular-nums">{money(s.total)}</p>
          {s.byCategory.length > 0 && (
            <p className="mt-1 truncate text-xs text-muted">
              {s.byCategory.slice(0, 3).map((b) => `${b.label} ${money(b.total)}`).join(" · ")}
            </p>
          )}
          {(iOwe > 0 || owedToMe > 0) && (
            <Link to="/expenses/owed" className="mt-2 flex gap-4 border-t border-border pt-2 text-sm">
              {iOwe > 0 && <span>You owe <b className="tabular-nums">{money(iOwe)}</b></span>}
              {owedToMe > 0 && <span>Owed to you <b className="tabular-nums">{money(owedToMe)}</b></span>}
            </Link>
          )}
        </Card>
      )}
      <BillsDue />
      <DueList limit={4} />
    </>
  );
}

/** Bills due within a week (or overdue) and expected bills not uploaded yet. Hidden when there are none. */
function BillsDue() {
  const items = (useUpcoming(7).data ?? []).filter((u) => u.kind === "bill" || u.missing);
  if (!items.length) return null;
  const today = todayISO();
  return (
    <Card title={<span className="flex items-center gap-2"><CalendarClock className="size-4 text-accent" />Bills</span>}
      action={<Link to="/expenses/bills" className="text-xs text-accent">All bills →</Link>}>
      <ul className="flex flex-col gap-1 text-sm">
        {items.map((u, i) => (
          <li key={i}>
            <Link to={u.receiptId ? `/expenses/receipts/${u.receiptId}` : "/expenses/bills"} className="flex items-center gap-2 py-1">
              {(u.overdue || u.missing) && <AlertTriangle className="size-4 shrink-0 text-danger" />}
              <span className="min-w-0 flex-1 truncate">{u.name}</span>
              <span className={cx("shrink-0 text-xs", u.overdue || u.missing ? "text-danger" : "text-muted")}>
                {u.missing ? "not uploaded" : u.overdue ? "overdue" : u.date === today ? "due today" : `due ${shortDate(u.date)}`}
              </span>
              {u.amount != null && <span className="w-16 shrink-0 text-right tabular-nums">{money(u.amount)}</span>}
            </Link>
          </li>
        ))}
      </ul>
    </Card>
  );
}
