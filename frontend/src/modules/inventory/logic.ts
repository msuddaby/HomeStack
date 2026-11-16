import { addDays, daysUntil, duration, shortDate, todayISO } from "@/core/dates";
import type { Tone } from "@/core/ui";
import type { Category, Item, LocationKind, Product } from "./api";

type ShelfLife = Pick<Product, "pantryDays" | "fridgeDays" | "freezerDays">;

export function shelfLifeDays(cat: Category | undefined, kind: LocationKind, p?: Partial<ShelfLife>): number | null {
  if (kind === "fridge") return p?.fridgeDays ?? cat?.fridgeDays ?? null;
  if (kind === "freezer") return p?.freezerDays ?? cat?.freezerDays ?? null;
  return p?.pantryDays ?? cat?.pantryDays ?? null;
}

/** Mirrors the server's suggestion so the form can preview it. */
export function suggestExpiry(cat: Category | undefined, kind: LocationKind, purchasedAt: string, frozenAt: string | null, p?: Partial<ShelfLife>) {
  const days = shelfLifeDays(cat, kind, p);
  if (days == null) return null;
  return addDays(kind === "freezer" ? frozenAt || purchasedAt : purchasedAt, days);
}

export interface Status { label: string; tone: Tone }

export function itemStatus(item: Item, today = todayISO()): Status | null {
  if (item.location.kind === "freezer") {
    const since = item.frozenAt ?? item.purchasedAt;
    if (item.expiresAt && daysUntil(item.expiresAt, today) < 0) return { label: `Frozen ${since ? duration(-daysUntil(since, today)) : ""} · past best`, tone: "warn" };
    return since ? { label: `Frozen ${duration(Math.max(0, -daysUntil(since, today)))}`, tone: "frost" } : { label: "Frozen", tone: "frost" };
  }
  if (!item.expiresAt) return null;
  const d = daysUntil(item.expiresAt, today);
  if (d < 0) return { label: d === -1 ? "Expired yesterday" : `Expired ${-d}d ago`, tone: "danger" };
  if (d === 0) return { label: "Expires today", tone: "danger" };
  if (d === 1) return { label: "Tomorrow", tone: "warn" };
  if (d <= 3) return { label: `${d} days`, tone: "warn" };
  if (d <= 14) return { label: `${d} days`, tone: "neutral" };
  return { label: shortDate(item.expiresAt), tone: "neutral" };
}

export const formatMoney = (n: number) => `$${n.toFixed(2)}`;

export function formatQty(q: number, unit: string | null) {
  const n = Number.isInteger(q) ? String(q) : q.toFixed(2).replace(/\.?0+$/, "");
  return unit ? `${n} ${unit}` : n === "1" ? "" : `×${n}`;
}

const GUESSES: [string, RegExp][] = [
  ["cheese", /chees|cheddar|mozzarella|parmesan|brie|feta|gouda/],
  ["eggs", /\beggs?\b/],
  ["ground-meat", /ground|mince/],
  ["poultry", /chicken|turkey|duck|wings?\b|drumstick/],
  ["seafood", /salmon|fish|shrimp|prawn|cod|tilapia|scallop|crab|lobster/],
  ["deli", /\bham\b|bacon|salami|sausage|deli|pepperoni|prosciutto|hot ?dog/],
  ["meat", /beef|steak|pork|lamb|ribs?\b|chops?\b|roast|veal|brisket/],
  ["leftovers", /leftover/],
  ["frozen", /frozen|ice cream|pizza|popsicle/],
  ["dairy", /milk|yog(h)?urt|cream|butter|kefir/],
  ["bakery", /bread|bagel|buns?\b|tortilla|muffin|croissant|pita|naan|rolls?\b/],
  ["canned", /\bcan(ned)?\b|soup|beans in/],
  ["condiments", /ketchup|mustard|mayo|sauce|dressing|jam|salsa|relish|syrup|honey|vinegar|oil\b/],
  ["beverages", /juice|soda|water|coffee|\btea\b|beer|wine|pop\b|kombucha/],
  ["snacks", /chips|cookie|cracker|chocolate|snack|candy|popcorn|pretzel|granola bar/],
  ["fruit", /apple|banana|berr|grape|orange|lemon|lime|melon|peach|pear|plum|mango|pineapple|kiwi|cherr|avocado|fruit/],
  ["vegetables", /lettuce|spinach|carrot|broccoli|onion|pepper|tomato|potato|cucumber|celery|kale|mushroom|zucchini|cabbage|garlic|corn|peas?\b|veg|salad/],
  ["dry-goods", /rice|pasta|spaghetti|noodle|flour|\boats?\b|cereal|sugar|lentil|quinoa|couscous|baking/],
];

export function guessCategory(name: string): string | null {
  const n = name.toLowerCase();
  return GUESSES.find(([, re]) => re.test(n))?.[0] ?? null;
}

export const KIND_LABEL: Record<LocationKind, string> = { pantry: "Pantry", fridge: "Fridge", freezer: "Freezer", other: "Other" };
