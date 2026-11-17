import { useState } from "react";
import { Link } from "react-router";
import { AlertTriangle, CalendarClock, Check, FileText, Plus, Repeat } from "lucide-react";
import { shortDate, todayISO } from "@/core/dates";
import { Button, Card, Chip, cx, Empty, Spinner } from "@/core/ui";
import { useBillPaid, useReceipts, useRecurring, useUpcoming, type Recurring, type Upcoming } from "./api";
import { BillSheet } from "./BillSheet";
import { money } from "./format";
import { RecurringSheet } from "./RecurringSheet";

const every: Record<string, string> = { weekly: "weekly", biweekly: "every 2 weeks", monthly: "monthly", yearly: "yearly" };

export function BillsPage() {
  const upcoming = useUpcoming(30);
  const recurring = useRecurring();
  const receipts = useReceipts();
  const [uploading, setUploading] = useState(false);
  const [editing, setEditing] = useState<Recurring | null | "new">(null);
  const bills = (receipts.data ?? []).filter((r) => r.source === "bill").slice(0, 10);

  return (
    <div className="flex flex-col gap-4">
      <Card title={<span className="flex items-center gap-2"><CalendarClock className="size-4 text-accent" />Coming up</span>}
        action={<Button variant="ghost" className="min-h-9 px-2 text-xs" onClick={() => setUploading(true)}><FileText className="size-4" />Upload a bill</Button>}>
        {!upcoming.data ? <Spinner /> : upcoming.data.length ? (
          <ul className="divide-y divide-border">{upcoming.data.map((u, i) => <UpcomingRow key={i} u={u} onUpload={() => setUploading(true)} />)}</ul>
        ) : <p className="text-sm text-muted">Nothing due in the next 30 days.</p>}
      </Card>

      <Card title={<span className="flex items-center gap-2"><Repeat className="size-4 text-accent" />Regular payments</span>}
        action={<Button variant="ghost" className="min-h-9 px-2 text-xs" onClick={() => setEditing("new")}><Plus className="size-4" />Add</Button>}>
        {!recurring.data ? <Spinner /> : recurring.data.length ? (
          <ul className="divide-y divide-border">
            {recurring.data.map((r) => (
              <li key={r.id}>
                <button onClick={() => setEditing(r)} className={cx("flex w-full items-center gap-3 py-2.5 text-left", !r.active && "opacity-50")}>
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-medium">{r.name}</p>
                    <p className="truncate text-xs text-muted">
                      {r.expected ? "Amount changes, upload each bill" : `${money(r.amount!)} ${every[r.frequency]}, recorded automatically`}
                      {r.split.length > 0 && ` · split ${r.split.length} ways`}
                    </p>
                  </div>
                  <span className="shrink-0 text-right text-xs text-muted">{r.active ? <>Next<br />{shortDate(r.nextDate)}</> : "Paused"}</span>
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-muted">
            Add rent or anything else that's the same each time and it's recorded on the day, split included.
            Bills that change (electricity, internet) can be added too, to be reminded when one hasn't been uploaded.
          </p>
        )}
      </Card>

      <Card title="Recent bills">
        {!receipts.data ? <Spinner /> : bills.length ? (
          <ul className="divide-y divide-border">
            {bills.map((b) => (
              <li key={b.id}>
                <Link to={`/expenses/receipts/${b.id}`} className="flex items-center gap-3 py-2.5">
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-medium">{b.store ?? "Bill"}</p>
                    <p className="text-xs text-muted">{shortDate(b.purchasedAt)}{b.dueDate && ` · due ${shortDate(b.dueDate)}`}</p>
                  </div>
                  {b.billPaidOn ? <Chip tone="accent">Paid</Chip> : b.dueDate && b.dueDate < todayISO() ? <Chip tone="danger">Overdue</Chip> : <Chip tone="warn">Unpaid</Chip>}
                  <span className="w-20 text-right font-medium tabular-nums">{money(b.total)}</span>
                </Link>
              </li>
            ))}
          </ul>
        ) : <Empty title="No bills yet">Upload a PDF or a photo of one.</Empty>}
      </Card>

      <BillSheet open={uploading} onClose={() => setUploading(false)} />
      <RecurringSheet open={editing !== null} recurring={editing === "new" ? null : editing} onClose={() => setEditing(null)} />
    </div>
  );
}

function UpcomingRow({ u, onUpload }: { u: Upcoming; onUpload: () => void }) {
  const paid = useBillPaid();
  const when = u.date === todayISO() ? "today" : shortDate(u.date);
  return (
    <li className="flex items-center gap-3 py-2.5">
      <div className="min-w-0 flex-1">
        {u.receiptId ? <Link to={`/expenses/receipts/${u.receiptId}`} className="block truncate font-medium">{u.name}</Link> : <p className="truncate font-medium">{u.name}</p>}
        <p className={cx("flex items-center gap-1 text-xs", u.missing || u.overdue ? "text-danger" : "text-muted")}>
          {(u.missing || u.overdue) && <AlertTriangle className="size-3.5" />}
          {u.kind === "bill" ? (u.overdue ? `Overdue since ${when}` : `Due ${when}`)
            : u.kind === "expected" ? (u.missing ? `Expected around ${when}, not uploaded yet` : `Bill expected around ${when}`)
            : `Recorded automatically ${when}`}
        </p>
      </div>
      {u.amount != null && <span className="shrink-0 font-medium tabular-nums">{money(u.amount)}</span>}
      {u.kind === "bill" && u.receiptId && (
        <Button variant="ghost" className="min-h-9 px-2 text-xs" busy={paid.isPending}
          onClick={() => paid.mutate({ id: u.receiptId!, paidOn: todayISO() })}><Check className="size-4" />Paid</Button>
      )}
      {u.kind === "expected" && <Button variant="ghost" className="min-h-9 px-2 text-xs" onClick={onUpload}>Upload</Button>}
    </li>
  );
}
