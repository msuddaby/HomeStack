import { useState } from "react";
import { Flame } from "lucide-react";
import { Card, cx, Empty, ErrorText, Spinner } from "@/core/ui";
import { useCategories, useWasteStats, type WasteStats } from "./api";
import { formatMoney } from "./logic";
import { ProductSheet } from "./ProductSheet";

type Month = WasteStats["months"][number];

const monthName = (key: string, style: "short" | "long" = "long") =>
  new Date(`${key}-01T12:00:00`).toLocaleDateString(undefined, { month: style, ...(style === "long" ? { year: "numeric" } : {}) });

const things = (n: number) => `${n} ${n === 1 ? "thing" : "things"}`;

export function WastePage() {
  const stats = useWasteStats(6);
  const categories = useCategories().data ?? [];
  const [selected, setSelected] = useState<string | null>(null);
  const [productId, setProductId] = useState<number | null>(null);

  if (stats.isPending) return <Spinner />;
  if (stats.error) return <ErrorText error={stats.error} />;

  const { months, topWasted } = stats.data;
  const current = months.find((m) => m.month === selected) ?? months[months.length - 1]!;
  const totalWasted = months.reduce((s, m) => s + m.wasted, 0);
  const totalUsed = months.reduce((s, m) => s + m.used, 0);
  const share = totalWasted + totalUsed > 0 ? Math.round((100 * totalWasted) / (totalWasted + totalUsed)) : null;
  const isThisMonth = current === months[months.length - 1];

  if (totalWasted === 0 && totalUsed === 0)
    return <Empty icon={<Flame className="size-8" />} title="Nothing yet">When you mark food as used or thrown out, it adds up here.</Empty>;

  return (
    <>
      <div className="flex flex-col gap-4">
        <Card>
          <p className="text-xs font-medium text-muted">{isThisMonth ? "This month" : monthName(current.month)}</p>
          <p className="mt-1 text-2xl font-bold">
            {things(current.wasted)} thrown out
            {current.wastedValue != null && <span className="text-muted"> · {formatMoney(current.wastedValue)}</span>}
          </p>
          <p className="mt-1 text-sm text-muted">
            {current.used} used{current.usedValue != null && ` (${formatMoney(current.usedValue)})`}
          </p>
          <WasteBars months={months} selected={current.month} onSelect={setSelected} />
          {share != null && (
            <p className="mt-3 border-t border-border pt-3 text-sm">
              Over 6 months, <b>{share}%</b> of what left the kitchen was thrown out rather than eaten.
            </p>
          )}
        </Card>

        {topWasted.length > 0 && (
          <Card title="Thrown out most">
            <p className="-mt-1 mb-2 text-xs text-muted">Tap one to lower its minimum or change how long it keeps.</p>
            <ul className="divide-y divide-border text-sm">
              {topWasted.map((x) => (
                <li key={x.product.id}>
                  <button onClick={() => setProductId(x.product.id)} className="flex w-full items-center gap-2 py-2 text-left">
                    <span>{categories.find((c) => c.id === x.product.category)?.emoji}</span>
                    <span className="min-w-0 flex-1 truncate">{x.product.name}</span>
                    <span className="text-muted">{x.times}×{x.value != null && ` · ${formatMoney(x.value)}`}</span>
                  </button>
                </li>
              ))}
            </ul>
          </Card>
        )}
        <p className="text-center text-xs text-muted">Dollar amounts only count items with a price (receipts, or entered by hand).</p>
      </div>
      <ProductSheet productId={productId} onClose={() => setProductId(null)} />
    </>
  );
}

/** One bar per month (things thrown out). Tap a bar to show that month above. */
function WasteBars({ months, selected, onSelect }: { months: Month[]; selected: string; onSelect: (m: string) => void }) {
  const max = Math.max(1, ...months.map((m) => m.wasted));
  return (
    <figure className="mt-4">
      <div className="flex h-32 items-end gap-0.5 border-b border-border" aria-hidden="true">
        {months.map((m) => (
          <button key={m.month} onClick={() => onSelect(m.month)} title={`${monthName(m.month)}: ${things(m.wasted)} thrown out`}
            className="group flex h-full flex-1 items-end justify-center px-1.5">
            <span className={cx("w-full max-w-10 rounded-t-[4px] transition",
              m.month === selected ? "bg-danger" : "bg-danger/40 group-hover:bg-danger/60")}
              style={{ height: m.wasted ? `${(100 * m.wasted) / max}%` : "2px" }} />
          </button>
        ))}
      </div>
      <div className="mt-1 flex gap-0.5" aria-hidden="true">
        {months.map((m) => (
          <span key={m.month} className={cx("flex-1 text-center text-xs", m.month === selected ? "font-semibold text-text" : "text-muted")}>
            {monthName(m.month, "short")}
          </span>
        ))}
      </div>
      {/* The same numbers for screen readers. */}
      <table className="sr-only">
        <caption>Things thrown out per month</caption>
        <thead><tr><th>Month</th><th>Thrown out</th><th>Value</th><th>Used</th></tr></thead>
        <tbody>
          {months.map((m) => (
            <tr key={m.month}><td>{monthName(m.month)}</td><td>{m.wasted}</td><td>{m.wastedValue != null ? formatMoney(m.wastedValue) : "–"}</td><td>{m.used}</td></tr>
          ))}
        </tbody>
      </table>
    </figure>
  );
}
