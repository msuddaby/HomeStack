import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/core/api";
import type { AddItem, LocationKind, Product } from "@/modules/inventory/api";

export interface ExpenseCategory { id: string; label: string; emoji: string }

/** A tracked item next to a receipt line: "Tide Pods, last $27.99 at Costco". */
export interface ItemRef { id: number; name: string; lastStore: string | null; lastUnitPrice: number | null; skipInventory: boolean }

export interface DraftLine {
  key: number; code: string | null; raw: string | null; name: string; category: string; quantity: number; unit: string | null;
  unitPrice: number | null; amount: number; discount: number | null;
  /** Bought before (same store + item number, or same name): linked automatically. */
  item: ItemRef | null;
  /** The AI thinks it's this item from another store; only linked if confirmed. */
  suggestion: ItemRef | null;
  /** Food and drink, which can go into inventory. `add` is the starting tick: off if it was left out last time. */
  food: { name: string; category: string; location: LocationKind; product: Product | null; add: boolean } | null;
}
export interface ReceiptDraft {
  store: string | null; purchasedAt: string | null; subtotal: number | null; tax: number | null; total: number | null;
  lines: DraftLine[]; note: string | null; duplicateOf: number | null;
}

export interface SaveLine {
  itemId: number | null; code: string | null; name: string; raw: string | null; category: string; quantity: number; unit: string | null;
  unitPrice: number | null; amount: number; discount: number | null; inventory: AddItem | null;
  /** Food lines: whether "Put away" was ticked (remembered for next time). Null for everything else. */
  trackInventory: boolean | null;
}
export interface SaveReceipt {
  store: string | null; purchasedAt: string; subtotal: number | null; tax: number | null; total: number; note: string | null; lines: SaveLine[];
}

export interface ReceiptLine {
  id: number; itemId: number | null; code: string | null; name: string; raw: string | null; category: string; quantity: number;
  unit: string | null; unitPrice: number | null; amount: number; discount: number | null;
}
export type ReceiptSource = "scan" | "manual";
export interface ReceiptSummary { id: number; store: string | null; purchasedAt: string; total: number; lineCount: number; source: ReceiptSource; hasPhoto: boolean }
export interface Receipt {
  id: number; store: string | null; purchasedAt: string; subtotal: number | null; tax: number | null; total: number; note: string | null;
  source: ReceiptSource; images: string[]; lines: ReceiptLine[]; createdAt: string; addedToInventory: number;
}

export interface Rhythm { purchases: number; lastBought: string; daysSinceLast: number; everyDays: number | null; due: boolean }
export interface TrackedItem {
  id: number; name: string; category: string; productId: number | null; rhythm: Rhythm | null;
  lastUnitPrice: number | null; lastStore: string | null; codes: string[];
  /** Food that isn't kept in inventory (a case of pop): the scanner starts it unticked. */
  skipInventory: boolean;
}
export interface StorePrice { store: string; lastUnitPrice: number | null; lowestUnitPrice: number | null; lastBought: string; purchases: number }
export interface Purchase {
  receiptId: number; date: string; store: string | null; quantity: number; unit: string | null; unitPrice: number | null; amount: number; discount: number | null;
}
export interface TrackedItemDetail { item: TrackedItem; stores: StorePrice[]; purchases: Purchase[]; totalSpent: number }

export interface Breakdown { key: string; label: string; total: number }
export interface Summary {
  month: string; total: number; previousTotal: number | null; byCategory: Breakdown[]; byStore: Breakdown[];
  months: { month: string; total: number; receipts: number }[];
}

const KEY = ["expenses"] as const;

export const useExpenseCategories = () =>
  useQuery({ queryKey: [...KEY, "categories"], queryFn: () => api<ExpenseCategory[]>("/expenses/categories"), staleTime: Infinity });
export const useSummary = (month?: string) =>
  useQuery({ queryKey: [...KEY, "summary", month ?? "now"], queryFn: () => api<Summary>(`/expenses/summary?months=6${month ? `&month=${month}` : ""}`), placeholderData: (p) => p });
export const useReceipts = () => useQuery({ queryKey: [...KEY, "receipts"], queryFn: () => api<ReceiptSummary[]>("/expenses/receipts") });
export const useReceipt = (id: number) => useQuery({ queryKey: [...KEY, "receipt", id], queryFn: () => api<Receipt>(`/expenses/receipts/${id}`) });
export const useItems = (q: string) =>
  useQuery({ queryKey: [...KEY, "items", q], queryFn: () => api<TrackedItem[]>(`/expenses/items?q=${encodeURIComponent(q.trim())}`), placeholderData: (p) => p });
export const useItem = (id: number) => useQuery({ queryKey: [...KEY, "item", id], queryFn: () => api<TrackedItemDetail>(`/expenses/items/${id}`) });
export const useDueItems = () => useQuery({ queryKey: [...KEY, "due"], queryFn: () => api<TrackedItem[]>("/expenses/items/due") });

export const readReceipt = (form: FormData) => api<ReceiptDraft>("/expenses/read", { body: form });

/** Saving a receipt can put food into inventory, so inventory and shopping refresh too. */
function useExpensesMutation<TVars, TResult>(fn: (v: TVars) => Promise<TResult>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () => Promise.all(["expenses", "inventory", "shopping"].map((k) => qc.invalidateQueries({ queryKey: [k] }))),
  });
}

export const useSaveReceipt = () => useExpensesMutation((v: SaveReceipt) => api<Receipt>("/expenses/receipts", { body: v }));
export const useAddManual = () =>
  useExpensesMutation((v: { store: string | null; purchasedAt: string; amount: number; category: string; description: string; note: string | null }) =>
    api<Receipt>("/expenses/receipts/manual", { body: v }));
export const useUpdateReceipt = () =>
  useExpensesMutation(({ id, ...v }: { id: number; store: string | null; purchasedAt: string; note: string | null }) =>
    api<Receipt>(`/expenses/receipts/${id}`, { method: "PUT", body: v }));
export const useDeleteReceipt = () => useExpensesMutation((id: number) => api(`/expenses/receipts/${id}`, { method: "DELETE" }));
export const useAddReceiptPhotos = () =>
  useExpensesMutation(({ id, photos }: { id: number; photos: Blob[] }) => {
    const form = new FormData();
    photos.forEach((p) => form.append("images", p, "receipt.jpg"));
    return api<Receipt>(`/expenses/receipts/${id}/photos`, { body: form });
  });
export const useUpdateItem = () =>
  useExpensesMutation(({ id, ...v }: { id: number; name: string; category: string; skipInventory?: boolean }) =>
    api<TrackedItemDetail>(`/expenses/items/${id}`, { method: "PUT", body: v }));
export const useMergeItem = () =>
  useExpensesMutation(({ id, intoItemId }: { id: number; intoItemId: number }) =>
    api<TrackedItemDetail>(`/expenses/items/${id}/merge`, { body: { intoItemId } }));
