import { Link } from "react-router";
import { AlertTriangle, CalendarClock, HandCoins, Receipt, Users, Wallet } from "lucide-react";
import { shortDate, todayISO } from "@/core/dates";
import type { HomeCard, RestockSuggestion } from "@/core/modules";
import { Button, Card, cx } from "@/core/ui";
import { useBalances, useDueItems, useSummary, useUpcoming, type Balance } from "./api";
import { monthLabel, money, rhythmText } from "./format";

/** This month's spending at a glance. */
function MonthSpending() {
  const s = useSummary().data;
  if (!s) return null;
  return (
    <Card title={<span className="flex items-center gap-2"><Wallet className="size-4 text-accent" />Spent this month</span>}
      action={<Link to="/expenses" className="text-xs text-accent">Details →</Link>}>
      <p className="text-sm text-muted">{monthLabel(s.month)}</p>
      <p className="text-2xl font-bold tabular-nums">{money(s.total)}</p>
      {s.byCategory.length > 0 && (
        <p className="mt-1 truncate text-xs text-muted">
          {s.byCategory.slice(0, 3).map((b) => `${b.label} ${money(b.total)}`).join(" · ")}
        </p>
      )}
    </Card>
  );
}

function ScanReceipt() {
  return (
    <Link to="/expenses?add=receipt" className="block">
      <Button className="w-full"><Receipt className="size-4" />Scan a receipt</Button>
    </Link>
  );
}

/** Things bought regularly that are about due, for Home's shopping card. */
function useDueSuggestions(): RestockSuggestion[] {
  const due = useDueItems().data ?? [];
  return due.map((i) => ({ key: `due-${i.id}`, name: i.name, productId: i.productId, reason: rhythmText(i.rhythm!) }));
}

export const expensesHomeCards: HomeCard[] = [
  { id: "scan-receipt", section: "actions", order: 20, Component: ScanReceipt },
  { id: "bills-due", section: "attention", order: 10, Component: BillsDue },
  { id: "you-owe", section: "attention", order: 11, Component: YouOwe },
  { id: "owed-to-others", section: "glance", order: 11, Component: OwedToOthers },
  { id: "month-spending", section: "glance", order: 10, Component: MonthSpending },
];
export { useDueSuggestions };

/** Just what you owe, and for what. Hidden when you're square. */
function YouOwe() {
  const data = useBalances().data;
  const me = data?.myPersonId ?? null;
  const mine = (data?.balances ?? []).filter((b) => me != null && b.personId === me && b.outstanding > 0);
  if (!mine.length) return null;
  const total = mine.reduce((s, b) => s + b.outstanding, 0);
  return (
    <Card title={<span className="flex items-center gap-2"><HandCoins className="size-4 text-accent" />You owe</span>}
      action={<Link to="/expenses/owed" className="text-xs text-accent">Details →</Link>}>
      <p className="text-2xl font-bold tabular-nums">{money(total)}</p>
      <div className="mt-3 flex flex-col gap-4">
        {mine.map((b) => (
          <div key={b.owedToPersonId}>
            <p className="text-xs font-medium uppercase tracking-wide text-muted">
              To {b.owedTo}{mine.length > 1 && <> · <span className="tabular-nums">{money(b.outstanding)}</span></>}
            </p>
            <OwedItems b={b} />
          </div>
        ))}
      </div>
    </Card>
  );
}

/** Admins see every balance in the household; everyone else sees who owes them. Hidden when there's nothing. */
function OwedToOthers() {
  const data = useBalances().data;
  const me = data?.myPersonId ?? null;
  const admin = !!data?.canMarkReceived;
  // Owed to you first, then everyone else's. Your own debts are in "You owe".
  const list = (data?.balances ?? [])
    .filter((b) => b.outstanding > 0 && b.personId !== me && (admin || b.owedToPersonId === me))
    .sort((a, b) => Number(b.owedToPersonId === me) - Number(a.owedToPersonId === me) || b.outstanding - a.outstanding);
  if (!list.length) return null;
  const allMine = list.every((b) => b.owedToPersonId === me);
  return (
    <Card title={<span className="flex items-center gap-2"><Users className="size-4 text-accent" />{allMine ? "Owed to you" : "Who owes what"}</span>}
      action={<Link to="/expenses/owed" className="text-xs text-accent">All →</Link>}>
      <div className="flex flex-col divide-y divide-border">
        {list.map((b) => (
          <div key={`${b.personId}-${b.owedToPersonId}`} className="py-3 first:pt-0 last:pb-0">
            <div className="flex items-baseline gap-2">
              <p className="min-w-0 flex-1 truncate font-semibold">
                {b.person} <span className="font-normal text-muted">owes</span> {b.owedToPersonId === me ? "you" : b.owedTo}
              </p>
              <span className="text-lg font-bold tabular-nums">{money(b.outstanding)}</span>
            </div>
            <OwedItems b={b} />
          </div>
        ))}
      </div>
    </Card>
  );
}

/** The expenses behind a balance that aren't paid back yet. */
function OwedItems({ b }: { b: Balance }) {
  const items = b.items.filter((i) => !i.receivedAt);
  return (
    <>
      <ul className="mt-1.5 flex flex-col gap-0.5 rounded-xl bg-surface-2 px-3 py-1 text-sm">
        {items.slice(0, MAX_OWED).map((i) => (
          <li key={i.shareId}>
            <Link to={`/expenses/receipts/${i.receiptId}`} className="flex items-center gap-3 py-1.5">
              <span className="min-w-0 flex-1">
                <span className="block truncate">{i.what}</span>
                <span className="block text-xs text-muted">{shortDate(i.date)}</span>
              </span>
              <span className="shrink-0 tabular-nums">{money(i.amount)}</span>
            </Link>
          </li>
        ))}
      </ul>
      {items.length > MAX_OWED && (
        <Link to="/expenses/owed" className="mt-1 inline-block text-xs text-muted">+{items.length - MAX_OWED} more</Link>
      )}
    </>
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
