import { Link } from "react-router";
import { AlertTriangle, CalendarClock, HandCoins, Wallet } from "lucide-react";
import { shortDate, todayISO } from "@/core/dates";
import { Card, cx } from "@/core/ui";
import { useBalances, useSummary, useUpcoming, type Balance } from "./api";
import { DueList } from "./DueList";
import { monthLabel, money } from "./format";

export function ExpensesDashboardWidget() {
  const summary = useSummary();
  const s = summary.data;
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
        </Card>
      )}
      <Owed />
      <BillsDue />
      <DueList limit={4} />
    </>
  );
}

/** Every expense someone still owes for, and their portion, grouped by who owes whom. Hidden when nobody owes anything. */
function Owed() {
  const data = useBalances().data;
  const me = data?.myPersonId ?? null;
  // Yours first: what you owe, then what's owed to you, then everyone else's.
  const rank = (b: Balance) => (b.personId === me ? 0 : b.owedToPersonId === me ? 1 : 2);
  const list = (data?.balances ?? []).filter((b) => b.outstanding > 0).sort((a, b) => rank(a) - rank(b));
  if (!list.length) return null;
  return (
    <Card title={<span className="flex items-center gap-2"><HandCoins className="size-4 text-accent" />Owed</span>}
      action={<Link to="/expenses/owed" className="text-xs text-accent">All →</Link>}>
      <div className="flex flex-col gap-3">
        {list.map((b) => {
          const items = b.items.filter((i) => !i.receivedAt);
          return (
            <div key={`${b.personId}-${b.owedToPersonId}`}>
              <p className="flex items-baseline gap-2 text-sm font-semibold">
                <span className="min-w-0 flex-1 truncate">
                  {b.personId === me ? "You owe" : `${b.person} owes`} {b.owedToPersonId === me ? "you" : b.owedTo}
                </span>
                <span className="tabular-nums">{money(b.outstanding)}</span>
              </p>
              <ul className="mt-0.5 flex flex-col text-sm">
                {items.slice(0, MAX_OWED).map((i) => (
                  <li key={i.shareId}>
                    <Link to={`/expenses/receipts/${i.receiptId}`} className="flex items-center gap-2 py-1">
                      <span className="min-w-0 flex-1 truncate">{i.what}</span>
                      <span className="shrink-0 text-xs text-muted">{shortDate(i.date)}</span>
                      <span className="w-16 shrink-0 text-right tabular-nums">{money(i.amount)}</span>
                    </Link>
                  </li>
                ))}
              </ul>
              {items.length > MAX_OWED && (
                <Link to="/expenses/owed" className="text-xs text-muted">+{items.length - MAX_OWED} more</Link>
              )}
            </div>
          );
        })}
      </div>
    </Card>
  );
}

const MAX_OWED = 5;

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
