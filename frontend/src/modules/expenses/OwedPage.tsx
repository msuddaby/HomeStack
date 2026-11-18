import { useState } from "react";
import { Link } from "react-router";
import { shortDate } from "@/core/dates";
import { AnimatedNumber, MotionItem, useExitingList, useHold, useStableOrder } from "@/core/motion";
import { useToast } from "@/core/toast";
import { Button, Card, Check, Chip, cx, Empty, ErrorText, Spinner } from "@/core/ui";
import { BellRing, GitMerge } from "lucide-react";
import { canRemindAgain, remindedLabel, useBalances, useMarkReceived, usePeople, useRemind, type Balance, type OwedItem } from "./api";
import { personLabel } from "./PeopleSettings";
import { money } from "./format";

/** Who owes whom for split expenses. Admins tick shares off as they're paid back. */
export function OwedPage() {
  const balances = useBalances();
  const mark = useMarkReceived();
  const people = usePeople();
  const toast = useToast();
  const [showReceived, setShowReceived] = useState(false);
  // A share that's just been ticked stays put for a moment, showing it's received, before it folds away.
  const { hold, release, isHeld } = useHold(HOLD_MS);
  const all = useStableOrder(balances.data?.balances ?? [], (b) => `${b.personId}-${b.owedToPersonId}`);
  const visible = (i: OwedItem) => showReceived || !i.receivedAt || isHeld(i.shareId);
  const cards = useExitingList(all.filter((b) => showReceived || b.outstanding > 0 || b.items.some((i) => i.receivedAt && isHeld(i.shareId))),
    (b) => `${b.personId}-${b.owedToPersonId}`, 320);
  if (balances.error) return <ErrorText error={balances.error} />;
  if (!balances.data) return <Spinner />;
  const { canMarkReceived, myPersonId } = balances.data;
  const iOwe = sum(all.filter((b) => b.personId === myPersonId));
  const owedToMe = sum(all.filter((b) => b.owedToPersonId === myPersonId));

  const onMark = (b: Balance, i: OwedItem, received: boolean) => {
    if (received) hold(i.shareId); else release(i.shareId);
    mark.mutate({ shareId: i.shareId, received });
    if (received && !showReceived) {
      toast({
        message: b.personId === myPersonId ? `${money(i.amount)} marked as paid back` : `Got ${money(i.amount)} from ${b.person}`,
        action: { label: "Undo", onClick: () => mark.mutateAsync({ shareId: i.shareId, received: false }) },
      });
    }
  };

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
          <Card><p className="text-xs text-muted">You owe</p><AnimatedNumber value={iOwe} format={money} className="block text-xl font-bold tabular-nums" /></Card>
          <Card><p className="text-xs text-muted">Owed to you</p><AnimatedNumber value={owedToMe} format={money} className="block text-xl font-bold tabular-nums" /></Card>
        </div>
      )}
      {!cards.length ? (
        <div className="animate-fade-in">
          <Empty title="Nobody owes anything">Split an expense (who paid, and who it's shared with) and it shows up here.</Empty>
        </div>
      ) : (
        // Cards carry their own spacing (not the parent's gap), so a card folding away takes its gap with it.
        <div className="-mb-4">
          {cards.map(({ item: b, key, leaving, entering }) => (
            <MotionItem key={key} leaving={leaving} entering={entering}>
              <div className="pb-4">
                <BalanceCard b={b} me={myPersonId} canMark={canMarkReceived} visible={visible} onMark={(i, received) => onMark(b, i, received)} />
              </div>
            </MotionItem>
          ))}
        </div>
      )}
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
const HOLD_MS = 700;

function BalanceCard({ b, me, canMark, visible, onMark }: {
  b: Balance; me: number | null; canMark: boolean; visible: (i: OwedItem) => boolean; onMark: (i: OwedItem, received: boolean) => void;
}) {
  const who = b.personId === me ? "You owe" : `${b.person} owes`;
  const to = b.owedToPersonId === me ? "you" : b.owedTo;
  const items = useExitingList(useStableOrder(b.items, (i) => i.shareId).filter(visible), (i) => i.shareId);
  return (
    <Card title={<span>{who} {to}</span>}
      action={<AnimatedNumber value={b.outstanding} format={money}
        className={cx("font-bold tabular-nums transition-colors", b.outstanding > 0 ? "text-text" : "text-accent")} />}>
      <ul>
        {items.map(({ item: i, key, leaving, entering }) => (
          <MotionItem as="li" key={key} leaving={leaving} entering={entering} className="first:*:*:border-t-0">
            <div className="flex items-center gap-3 border-t border-border py-2 text-sm">
              {canMark && (
                <Check checked={!!i.receivedAt} label={`Received ${money(i.amount)} for ${i.what}`} onChange={(received) => onMark(i, received)} />
              )}
              <Link to={`/expenses/receipts/${i.receiptId}`}
                className={cx("min-w-0 flex-1 transition-colors duration-300", i.receivedAt && "text-muted line-through")}>
                <p className="truncate">{i.what}</p>
                <p className="text-xs text-muted">{shortDate(i.date)}</p>
              </Link>
              {i.receivedAt && <Chip tone="accent" className="animate-pop">Received</Chip>}
              <span className={cx("tabular-nums transition-colors duration-300", i.receivedAt && "text-muted")}>{money(i.amount)}</span>
            </div>
          </MotionItem>
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
