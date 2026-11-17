import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/core/api";
import type { AddItem, LocationKind, Product } from "@/modules/inventory/api";

export interface ExpenseCategory { id: string; label: string; emoji: string }

/** A tracked item next to a receipt line: "Tide Pods, last $27.99 at Costco". */
export interface ItemRef {
  id: number; name: string; lastStore: string | null; lastUnitPrice: number | null; skipInventory: boolean;
  /** How much one of these is ("2 L"), remembered from last time. */
  packQuantity: number | null; packUnit: string | null;
}
/** Where a put-away amount came from: remembered on the item, a size printed on the receipt, or just the receipt quantity. */
export type PackSource = "receipt" | "printed" | "remembered";

export interface DraftLine {
  key: number; code: string | null; raw: string | null; name: string; category: string; quantity: number; unit: string | null;
  unitPrice: number | null; amount: number; discount: number | null;
  /** Bought before (same store + item number, or same name): linked automatically. */
  item: ItemRef | null;
  /** The AI thinks it's this item from another store; only linked if confirmed. */
  suggestion: ItemRef | null;
  /**
   * Food and drink, which can go into inventory. `add` is the starting tick: off if it was left out last time.
   * `quantity`/`unit` is how much to put away (e.g. 2 L for a line that just says PACE SALSA).
   */
  food: {
    name: string; category: string; location: LocationKind; product: Product | null; add: boolean;
    quantity: number; unit: string | null; amountFrom: PackSource;
  } | null;
}
export interface ReceiptDraft {
  store: string | null; purchasedAt: string | null; subtotal: number | null; tax: number | null; total: number | null;
  lines: DraftLine[]; note: string | null; duplicateOf: number | null;
}

/** One person's part of a split: weight 1 each for equal, or the amounts themselves for custom amounts. */
export interface SplitPart { personId: number; weight: number }
export interface Person { id: number; name: string; userId: number | null; archived: boolean; isMe: boolean }
export interface Share { id: number; personId: number; person: string; amount: number; receivedAt: string | null }

export interface SaveLine {
  itemId: number | null; code: string | null; name: string; raw: string | null; category: string; quantity: number; unit: string | null;
  unitPrice: number | null; amount: number; discount: number | null; inventory: AddItem | null;
  /** Food lines: whether "Put away" was ticked (remembered for next time). Null for everything else. */
  trackInventory: boolean | null;
  /** Who this line is split between, when not the receipt's split. */
  split: SplitPart[] | null;
}
export interface SaveReceipt {
  store: string | null; purchasedAt: string; subtotal: number | null; tax: number | null; total: number; note: string | null; lines: SaveLine[];
  paidByPersonId: number | null; split: SplitPart[] | null;
}

export interface ReceiptLine {
  id: number; itemId: number | null; code: string | null; name: string; raw: string | null; category: string; quantity: number;
  unit: string | null; unitPrice: number | null; amount: number; discount: number | null; split: SplitPart[];
}
export type ReceiptSource = "scan" | "manual" | "bill" | "recurring";
export interface ReceiptSummary {
  id: number; store: string | null; purchasedAt: string; total: number; lineCount: number; source: ReceiptSource; hasPhoto: boolean;
  dueDate: string | null; billPaidOn: string | null;
  /** Shares still owed to whoever paid. */
  outstanding: number;
}
export interface Receipt {
  id: number; store: string | null; purchasedAt: string; subtotal: number | null; tax: number | null; total: number; note: string | null;
  source: ReceiptSource; images: string[]; lines: ReceiptLine[]; createdAt: string; addedToInventory: number;
  paidByPersonId: number | null; paidBy: string | null; split: SplitPart[]; shares: Share[];
  dueDate: string | null; billPaidOn: string | null; recurringId: number | null;
  /** The bill's original PDF, to open. */
  document: string | null;
}

export interface OwedItem { shareId: number; receiptId: number; what: string; date: string; amount: number; receivedAt: string | null }
export interface Balance { personId: number; person: string; owedToPersonId: number; owedTo: string; outstanding: number; items: OwedItem[] }
export interface Balances { canMarkReceived: boolean; myPersonId: number | null; balances: Balance[] }

export interface RecurringRef { id: number; name: string; paidByPersonId: number | null; split: SplitPart[] }
export interface BillDraft {
  payee: string | null; statementDate: string | null; dueDate: string | null; amount: number | null; category: string; account: string | null;
  periodStart: string | null; periodEnd: string | null; note: string | null;
  /** The expected bill it looks like (same payee); its split is the starting point. */
  recurring: RecurringRef | null; duplicateOf: number | null;
}
export interface SaveBill {
  payee: string; statementDate: string; dueDate: string | null; amount: number; category: string; description: string | null; note: string | null;
  paidByPersonId: number | null; split: SplitPart[] | null; recurringId: number | null; paidOn: string | null;
}

export type Frequency = "weekly" | "biweekly" | "monthly" | "yearly";
export interface Recurring {
  id: number; name: string; payee: string | null; category: string; amount: number | null; frequency: Frequency; nextDate: string;
  paidByPersonId: number | null; paidBy: string | null; split: SplitPart[]; active: boolean; note: string | null;
  /** No fixed amount: a bill uploaded each time. */
  expected: boolean;
  lastReceiptId: number | null; lastDate: string | null; lastAmount: number | null;
}
export interface RecurringInput {
  name: string; payee: string | null; category: string; amount: number | null; frequency: Frequency; nextDate: string;
  paidByPersonId: number | null; split: SplitPart[] | null; active: boolean; note: string | null;
}
export interface Upcoming {
  kind: "bill" | "recurring" | "expected"; date: string; name: string; amount: number | null; receiptId: number | null; recurringId: number | null;
  overdue: boolean; missing: boolean;
}

export interface Rhythm { purchases: number; lastBought: string; daysSinceLast: number; everyDays: number | null; due: boolean }
export interface TrackedItem {
  id: number; name: string; category: string; productId: number | null; rhythm: Rhythm | null;
  lastUnitPrice: number | null; lastStore: string | null; codes: string[];
  /** Food that isn't kept in inventory (a case of pop): the scanner starts it unticked. */
  skipInventory: boolean;
  /** How much one of these is, for putting away ("2 L"). */
  packQuantity: number | null; packUnit: string | null;
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
  useExpensesMutation((v: {
    store: string | null; purchasedAt: string; amount: number; category: string; description: string; note: string | null;
    paidByPersonId: number | null; split: SplitPart[] | null;
  }) => api<Receipt>("/expenses/receipts/manual", { body: v }));

// ---- People, splitting, who owes whom ----
export const usePeople = () => useQuery({ queryKey: [...KEY, "people"], queryFn: () => api<Person[]>("/expenses/people") });
export const useAddPerson = () => useExpensesMutation((name: string) => api<Person>("/expenses/people", { body: { name } }));
export const useRenamePerson = () =>
  useExpensesMutation(({ id, name }: { id: number; name: string }) => api<Person>(`/expenses/people/${id}`, { method: "PUT", body: { name } }));
export const useArchivePerson = () => useExpensesMutation((id: number) => api(`/expenses/people/${id}`, { method: "DELETE" }));
export const useSetSplit = () =>
  useExpensesMutation(({ id, ...v }: { id: number; paidByPersonId: number | null; split: SplitPart[] | null; lines?: { lineId: number; split: SplitPart[] | null }[] }) =>
    api<Receipt>(`/expenses/receipts/${id}/split`, { method: "PUT", body: v }));
export const useBalances = () => useQuery({ queryKey: [...KEY, "balances"], queryFn: () => api<Balances>("/expenses/balances") });
export const useMarkReceived = () =>
  useExpensesMutation(({ shareId, received }: { shareId: number; received: boolean }) =>
    api(`/expenses/shares/${shareId}/received`, { method: "PUT", body: { received } }));

// ---- Bills ----
export const readBill = (form: FormData) => api<BillDraft>("/expenses/bills/read", { body: form });
export const useSaveBill = () => useExpensesMutation((v: SaveBill) => api<Receipt>("/expenses/bills", { body: v }));
export const useAttachDocument = () =>
  useExpensesMutation(({ id, file, name }: { id: number; file: Blob; name: string }) => {
    const form = new FormData();
    form.append("file", file, name);
    return api<Receipt>(`/expenses/receipts/${id}/document`, { body: form });
  });
export const useBillPaid = () =>
  useExpensesMutation(({ id, paidOn }: { id: number; paidOn: string | null }) =>
    api<Receipt>(`/expenses/receipts/${id}/bill-paid`, { method: "PUT", body: { paidOn } }));

// ---- Recurring payments and expected bills ----
export const useRecurring = () => useQuery({ queryKey: [...KEY, "recurring"], queryFn: () => api<Recurring[]>("/expenses/recurring") });
export const useSaveRecurring = () =>
  useExpensesMutation(({ id, ...v }: RecurringInput & { id?: number }) =>
    api<Recurring>(id ? `/expenses/recurring/${id}` : "/expenses/recurring", { method: id ? "PUT" : "POST", body: v }));
export const useDeleteRecurring = () => useExpensesMutation((id: number) => api(`/expenses/recurring/${id}`, { method: "DELETE" }));
export const useUpcoming = (days = 30) =>
  useQuery({ queryKey: [...KEY, "upcoming", days], queryFn: () => api<Upcoming[]>(`/expenses/upcoming?days=${days}`) });
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
  useExpensesMutation(({ id, ...v }: {
    id: number; name: string; category: string; skipInventory?: boolean;
    /** With updatePack: how much one is; null forgets it. */
    packQuantity?: number | null; packUnit?: string | null; updatePack?: boolean;
  }) =>
    api<TrackedItemDetail>(`/expenses/items/${id}`, { method: "PUT", body: v }));
export const useMergeItem = () =>
  useExpensesMutation(({ id, intoItemId }: { id: number; intoItemId: number }) =>
    api<TrackedItemDetail>(`/expenses/items/${id}/merge`, { body: { intoItemId } }));
