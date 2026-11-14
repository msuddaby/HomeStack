const FRACTIONS: [number, string][] = [[0.125, "⅛"], [0.25, "¼"], [1 / 3, "⅓"], [0.5, "½"], [2 / 3, "⅔"], [0.75, "¾"]];

/** 1.5 -> "1½", 0.333 -> "⅓", 2.4 -> "2.4" */
export function prettyAmount(n: number): string {
  const whole = Math.floor(n);
  const frac = n - whole;
  if (frac < 0.02) return String(whole);
  const f = FRACTIONS.find(([v]) => Math.abs(v - frac) < 0.02);
  if (f) return whole ? `${whole}${f[1]}` : f[1];
  return String(Math.round(n * 100) / 100);
}

export function minutes(m: number | null | undefined): string | null {
  if (!m) return null;
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60), r = m % 60;
  return r ? `${h} h ${r} min` : `${h} h`;
}

export const SLOT_LABEL = { breakfast: "Breakfast", lunch: "Lunch", dinner: "Dinner", snack: "Snack" } as const;
