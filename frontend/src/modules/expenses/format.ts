import type { Rhythm } from "./api";

export const money = (n: number) =>
  `${n < 0 ? "−" : ""}$${Math.abs(n).toLocaleString("en-CA", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

/** "2026-11" → "November 2026". */
export function monthLabel(month: string, short = false) {
  const [y, m] = month.split("-").map(Number);
  return new Date(y!, m! - 1, 1).toLocaleDateString(undefined, short ? { month: "short" } : { month: "long", year: "numeric" });
}

/** "2026-11" moved by n months. */
export function shiftMonth(month: string, n: number) {
  const [y, m] = month.split("-").map(Number);
  const d = new Date(y!, m! - 1 + n, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

/** "Every ~24 days · last bought 20 days ago". */
export function rhythmText(r: Rhythm) {
  const last = r.daysSinceLast === 0 ? "bought today" : r.daysSinceLast === 1 ? "bought yesterday" : `last bought ${r.daysSinceLast} days ago`;
  return r.everyDays && r.purchases >= 2 ? `Every ~${r.everyDays} days · ${last}` : `Bought ${r.purchases === 1 ? "once" : `${r.purchases} times`} · ${last}`;
}
