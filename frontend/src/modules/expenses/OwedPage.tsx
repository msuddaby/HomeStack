import { useState } from "react";
import { Link } from "react-router";
import { shortDate } from "@/core/dates";
import { Button, Card, Chip, cx, Empty, ErrorText, Spinner } from "@/core/ui";
import { BellRing, GitMerge } from "lucide-react";
import { canRemindAgain, remindedLabel, useBalances, useMarkReceived, usePeople, useRemind, type Balance } from "./api";
import { personLabel } from "./PeopleSettings";
import { money } from "./format";

/** Who owes whom for split expenses. Admins tick shares off as they're paid back. */
export function OwedPage() {
  const balances = useBalances();
  const mark = useMarkReceived();
  const people = usePeople();
  const [showReceived, setShowReceived] = useState(false);
  if (balances.error) return <ErrorText error={balances.error} />;
  if (!balances.data) return <Spinner />;
  const { canMarkReceived, myPersonId } = balances.data;
  const list = balances.data.balances.filter((b) => showReceived || b.outstanding > 0);
  const iOwe = sum(balances.data.balances.filter((b) => b.personId === myPersonId));
  const owedToMe = sum(balances.data.balances.filter((b) => b.owedToPersonId === myPersonId));

  return (
    <div className="flex flex-col gap-4">
      {canMarkReceived && (people.data ?? []).filter((p) => p.sameAsId != null).map((p) => (
        <Link key={p.id} to="/settings" className="flex items-center gap-3 rounded-2xl border border-border bg-surface-2 p-3 text-sm">
          <GitMerge className="size-5 shrink-0 text-accent" />
          <span><b>{personLabel(p)}</b> may be {people.data?.find((m) => m.id === p.sameAsId)?.name}. Merge them in Settings.</span>
        </Link>
      ))}
      {myPersonId != null && (iOwe > 0 || owedToMe > 0) && (
        <div className="grid grid-cols-2 gap-2">
          <Card><p className="text-xs text-muted">You owe</p><p className="text-xl font-bold tabular-nums">{money(iOwe)}</p></Card>
          <Card><p className="text-xs text-muted">Owed to you</p><p className="text-xl font-bold tabular-nums">{money(owedToMe)}</p></Card>
        </div>
      )}
      {!list.length ? (
        <Empty title="Nobody owes anything">Split an expense (who paid, and who it's shared with) and it shows up here.</Empty>
      ) : list.map((b) => (
        <BalanceCard key={`${b.personId}-${b.owedToPersonId}`} b={b} me={myPersonId} canMark={canMarkReceived} showReceived={showReceived}
          onMark={(shareId, received) => mark.mutate({ shareId, received })} busyId={mark.isPending ? mark.variables?.shareId : undefined} />
      ))}
      {mark.error && <ErrorText error={mark.error} />}
      <label className="flex items-center gap-2 text-xs text-muted">
        <input type="checkbox" checked={showReceived} onChange={(e) => setShowReceived(e.target.checked)} className="size-4 accent-[var(--color-accent)]" />
        Show what's been paid back (last 30 days)
      </label>
      {!canMarkReceived && <p className="text-xs text-muted">Only a household admin can mark payments as received.</p>}
    </div>
  );
}

const sum = (bs: Balance[]) => bs.reduce((s, b) => s + b.outstanding, 0);

function BalanceCard({ b, me, canMark, showReceived, onMark, busyId }: {
  b: Balance; me: number | null; canMark: boolean; showReceived: boolean; onMark: (shareId: number, received: boolean) => void; busyId?: number;
}) {
  const who = b.personId === me ? "You owe" : `${b.person} owes`;
  const to = b.owedToPersonId === me ? "you" : b.owedTo;
  const items = b.items.filter((i) => showReceived || !i.receivedAt);
  return (
    <Card title={<span>{who} {to}</span>} action={<span className={cx("font-bold tabular-nums", b.outstanding > 0 ? "text-text" : "text-muted")}>{money(b.outstanding)}</span>}>
      <ul className="divide-y divide-border">
        {items.map((i) => (
          <li key={i.shareId} className="flex items-center gap-3 py-2 text-sm">
            {canMark && (
              <input type="checkbox" checked={!!i.receivedAt} disabled={busyId === i.shareId} aria-label={`Received ${money(i.amount)} for ${i.what}`}
                onChange={(e) => onMark(i.shareId, e.target.checked)} className="size-5 shrink-0 accent-[var(--color-accent)]" />
            )}
            <Link to={`/expenses/receipts/${i.receiptId}`} className={cx("min-w-0 flex-1", i.receivedAt && "text-muted line-through")}>
              <p className="truncate">{i.what}</p>
              <p className="text-xs text-muted">{shortDate(i.date)}</p>
            </Link>
            {i.receivedAt && <Chip tone="accent">Received</Chip>}
            <span className="tabular-nums">{money(i.amount)}</span>
          </li>
        ))}
      </ul>
      {canMark && b.isMember && b.outstanding > 0 && b.personId !== me && <RemindRow b={b} />}
    </Card>
  );
}

/** Admins send a notification to someone who still owes (everything they owe this person). */
function RemindRow({ b }: { b: Balance }) {
  const remind = useRemind();
  const last = b.items.filter((i) => !i.receivedAt).map((i) => i.remindedAt).filter((x): x is string => !!x).sort().at(-1) ?? null;
  return (
    <div className="mt-2 flex flex-wrap items-center gap-2 border-t border-border pt-2">
      <Button variant="ghost" className="min-h-9 px-2 text-xs" busy={remind.isPending} disabled={!canRemindAgain(last)}
        onClick={() => remind.mutate({ personId: b.personId, owedToPersonId: b.owedToPersonId })}>
        <BellRing className="size-4" />Remind {b.person}
      </Button>
      {last && <span className="text-xs text-muted">Reminded {remindedLabel(last)}</span>}
      {remind.error && <p className="w-full text-xs text-danger">{remind.error.message}</p>}
    </div>
  );
}
