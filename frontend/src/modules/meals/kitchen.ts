import type { Ingredient } from "./api";

/** A numbered step, with the section heading ("## For the sauce") it falls under, if any. */
export interface KitchenStep { number: number; text: string; section: string | null }

/** Recipe steps without the "## Heading" lines, each tagged with its section. */
export function kitchenSteps(steps: string[]): KitchenStep[] {
  const out: KitchenStep[] = [];
  let section: string | null = null;
  for (const s of steps) {
    if (s.startsWith("## ")) { section = s.slice(3).trim() || null; continue; }
    if (s.trim()) out.push({ number: out.length + 1, text: s.trim(), section });
  }
  return out;
}

/** A cooking time mentioned in a step, e.g. "simmer for 20–25 minutes". Timers start at the low end. */
export interface StepTimer { label: string; seconds: number }

const TIME = /\b(\d+(?:[.,]\d+)?|an?|one|half an?)\s*(?:(?:-|–|to)\s*(\d+(?:[.,]\d+)?)\s*)?(hours?|hrs?|minutes?|mins?|seconds?|secs?)\b/gi;
const UNIT_SECONDS = (u: string) => (/^h/i.test(u) ? 3600 : /^m/i.test(u) ? 60 : 1);
const unitLabel = (u: string) => (/^h/i.test(u) ? "h" : /^m/i.test(u) ? "min" : "s");

function amount(word: string): number {
  const w = word.toLowerCase();
  if (w === "a" || w === "an" || w === "one") return 1;
  if (w.startsWith("half")) return 0.5;
  return Number(w.replace(",", "."));
}

export function findTimers(text: string): StepTimer[] {
  const seen = new Set<number>();
  const timers: StepTimer[] = [];
  for (const m of text.matchAll(TIME)) {
    const low = amount(m[1]!);
    const per = UNIT_SECONDS(m[3]!);
    const seconds = Math.round(low * per);
    if (!seconds || seconds > 24 * 3600 || seen.has(seconds)) continue;
    seen.add(seconds);
    const lowText = m[1]!.toLowerCase().startsWith("half") ? "½" : /^(an?|one)$/i.test(m[1]!) ? "1" : m[1]!;
    timers.push({ label: `${lowText}${m[2] ? `–${m[2]}` : ""} ${unitLabel(m[3]!)}`, seconds });
  }
  return timers;
}

const STOP = new Set(["fresh", "large", "small", "medium", "chopped", "ground", "dried", "whole", "boneless", "skinless", "extra", "virgin"]);
/** Words that name a form, not the food: "basil leaves" is matched by "basil". */
const GENERIC = new Set(["leaf", "leaves", "clove", "cloves", "sprig", "sprigs", "stalk", "stalks", "piece", "pieces", "slice", "slices", "head", "bunch", "can", "jar", "tin"]);
const singular = (w: string) => w.replace(/(ies)$/, "y").replace(/(oes|ses|ches|shes)$/, (s) => s.slice(0, -2)).replace(/s$/, "");

/**
 * Ingredients a step mentions: the full name, or its last real word ("chicken thighs" -> "thigh", "basil leaves" -> "basil"),
 * ignoring plurals. Conservative on purpose; it only decides what's shown next to the step.
 */
export function mentionedIngredients(text: string, ingredients: Ingredient[]): Ingredient[] {
  const words = new Set(text.toLowerCase().split(/[^a-z]+/).filter(Boolean).map(singular));
  const lower = text.toLowerCase();
  return ingredients.filter((i) => {
    const name = i.name.toLowerCase().trim();
    if (name && lower.includes(name)) return true;
    const parts = name.split(/[^a-z]+/).filter((w) => w.length >= 3 && !STOP.has(w) && !GENERIC.has(w));
    const head = parts[parts.length - 1];
    return !!head && words.has(singular(head));
  });
}

/** "4:05", "1:02:30" */
export function clock(seconds: number): string {
  const s = Math.max(0, Math.ceil(seconds));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = s % 60;
  const mm = h ? String(m).padStart(2, "0") : String(m);
  return `${h ? `${h}:` : ""}${mm}:${String(r).padStart(2, "0")}`;
}
