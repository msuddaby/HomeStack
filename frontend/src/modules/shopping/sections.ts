/** Store sections, in a typical walk-through order. Maps inventory categories onto aisles. */
export const SECTIONS: { id: string; label: string; emoji: string; categories: string[] }[] = [
  { id: "produce", label: "Produce", emoji: "🥬", categories: ["fruit", "vegetables"] },
  { id: "bakery", label: "Bakery", emoji: "🍞", categories: ["bakery"] },
  { id: "deli", label: "Deli", emoji: "🥓", categories: ["deli"] },
  { id: "meat", label: "Meat & seafood", emoji: "🥩", categories: ["meat", "ground-meat", "poultry", "seafood"] },
  { id: "dairy", label: "Dairy & eggs", emoji: "🥛", categories: ["dairy", "cheese", "eggs"] },
  { id: "pantry", label: "Pantry", emoji: "🥫", categories: ["dry-goods", "canned", "condiments", "snacks"] },
  { id: "drinks", label: "Drinks", emoji: "🧃", categories: ["beverages"] },
  { id: "frozen", label: "Frozen", emoji: "🧊", categories: ["frozen"] },
  { id: "other", label: "Other", emoji: "🛒", categories: ["leftovers", "other"] },
];

const byCategory = new Map(SECTIONS.flatMap((s) => s.categories.map((c) => [c, s] as const)));
export const sectionFor = (category: string) => byCategory.get(category) ?? SECTIONS[SECTIONS.length - 1]!;

/** "2 milk", "3x eggs", "1.5 kg chicken" → quantity + rest. */
export function parseEntry(text: string): { name: string; quantity: number | null; unit: string | null } {
  const m = text.trim().match(/^(\d+(?:[.,]\d+)?)\s*(?:x\s+|×\s*)?(?:(kg|g|lb|lbs|oz|l|ml|pack|packs|bag|bags|can|cans|box|boxes|jar|jars|bottle|bottles|dozen)\s+)?(.+)$/i);
  if (!m) return { name: text.trim(), quantity: null, unit: null };
  return { quantity: Number(m[1]!.replace(",", ".")), unit: m[2]?.toLowerCase() ?? null, name: m[3]!.trim() };
}
