import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/core/api";
import { useToast } from "@/core/toast";

export type LocationKind = "pantry" | "fridge" | "freezer" | "other";
export interface Location { id: number; name: string; kind: LocationKind; sort: number }
export interface Category {
  id: string; label: string; emoji: string; defaultLocation: LocationKind;
  pantryDays: number | null; fridgeDays: number | null; freezerDays: number | null; thawedDays: number; freezable: boolean;
}
export interface Product {
  id: number; name: string; brand: string | null; category: string; barcode: string | null; defaultUnit: string | null;
  imageUrl: string | null; pantryDays: number | null; fridgeDays: number | null; freezerDays: number | null; minQuantity: number | null;
  /** What to show: the server's small copy when it has one, otherwise `imageUrl`. */
  image: string | null;
}
export interface Item {
  id: number; quantity: number; unit: string | null; purchasedAt: string | null; expiresAt: string | null;
  openedAt: string | null; frozenAt: string | null; notes: string | null; createdAt: string; product: Product; location: Location;
  /** Value of what's left, when the item has a price. */
  price: number | null;
}
/** Result of use / throw out / move. `item` is null when it's all gone; `eventId` undoes it. */
export interface ItemAction { item: Item | null; eventId: number }
export interface WasteStats {
  months: { month: string; wasted: number; used: number; wastedValue: number | null; usedValue: number | null }[];
  topWasted: { product: Product; times: number; value: number | null }[];
}
export interface Dashboard {
  expired: Item[]; expiringSoon: Item[]; freezeSoon: Item[];
  longInFreezer: { item: Item; daysFrozen: number; freezerDays: number }[];
  lowStock: { product: Product; quantity: number }[];
  counts: { locationId: number; name: string; kind: LocationKind; items: number }[];
}
export interface NewProduct {
  name: string; category: string; brand?: string | null; barcode?: string | null; defaultUnit?: string | null; imageUrl?: string | null;
  pantryDays?: number | null; fridgeDays?: number | null; freezerDays?: number | null; minQuantity?: number | null;
}
export interface AddItem {
  productId?: number; product?: NewProduct; locationId: number; quantity: number; unit?: string | null;
  purchasedAt?: string | null; expiresAt?: string | null; noExpiry?: boolean; openedAt?: string | null; frozenAt?: string | null; notes?: string | null;
  /** What was paid for this quantity. */
  price?: number | null;
}
export type UpdateItem = Omit<Required<AddItem>, "productId" | "product" | "noExpiry">;
export interface BarcodeLookup {
  source: "local" | "openfoodfacts" | "none"; product: Product | null;
  suggestion: { name: string; brand: string | null; category: string; imageUrl: string | null; quantity: string | null } | null;
}

/** What the AI read from a receipt or typed list, for review. `product` is set when it matches a known product. */
export interface ReadItem {
  name: string; brand: string | null; category: string; location: LocationKind; quantity: number; unit: string | null;
  price: number | null; rawText: string | null; product: Product | null;
}
export interface ReadResult { store: string | null; purchasedAt: string | null; items: ReadItem[]; skipped: string[]; note: string | null }

const KEY = ["inventory"] as const;

export const useCategories = () =>
  useQuery({ queryKey: [...KEY, "categories"], queryFn: () => api<Category[]>("/inventory/categories"), staleTime: Infinity });
export const useLocations = () =>
  useQuery({ queryKey: [...KEY, "locations"], queryFn: () => api<Location[]>("/inventory/locations") });
export const useDashboard = () =>
  useQuery({ queryKey: [...KEY, "dashboard"], queryFn: () => api<Dashboard>("/inventory/dashboard") });
export function useItems(filter: { locationId?: number; q?: string }) {
  const p = new URLSearchParams();
  if (filter.locationId) p.set("locationId", String(filter.locationId));
  if (filter.q) p.set("q", filter.q);
  return useQuery({ queryKey: [...KEY, "items", filter], queryFn: () => api<Item[]>(`/inventory/items?${p}`), placeholderData: (prev) => prev });
}
const searchProducts = (q: string, limit: number) =>
  api<Product[]>(`/inventory/products?q=${encodeURIComponent(q.trim())}&limit=${limit}`);
/** All products (optionally filtered), for the products page. */
export const useProducts = (q: string) =>
  useQuery({ queryKey: [...KEY, "products", "all", q], queryFn: () => searchProducts(q, 100), placeholderData: (prev) => prev });
/** Autocomplete: only runs once something is typed. */
export function useProductSearch(q: string, limit = 8) {
  return useQuery({
    queryKey: [...KEY, "products", q, limit],
    queryFn: () => searchProducts(q, limit),
    enabled: q.trim().length > 0,
    placeholderData: (prev) => prev,
  });
}
export const useProduct = (id: number | null) =>
  useQuery({ queryKey: [...KEY, "product", id], queryFn: () => api<Product>(`/inventory/products/${id}`), enabled: id != null });
export const useSaveProduct = () =>
  useInventoryMutation(({ id, ...v }: NewProduct & { id: number }) => api<Product>(`/inventory/products/${id}`, { method: "PUT", body: v }));

/** Uses a photo (already shrunk on the phone) as the product's picture. */
export const useSetProductImage = () =>
  useInventoryMutation(({ id, photo }: { id: number; photo: Blob }) => {
    const form = new FormData();
    form.append("image", photo, "photo.jpg");
    return api<Product>(`/inventory/products/${id}/image`, { body: form });
  });
export const useRemoveProductImage = () =>
  useInventoryMutation((id: number) => api<Product>(`/inventory/products/${id}/image`, { method: "DELETE" }));

export const lookupBarcode = (code: string) => api<BarcodeLookup>(`/inventory/barcode/${encodeURIComponent(code)}`);

/** Every inventory mutation refreshes all inventory queries (lists, dashboard, counts) and the shopping list. */
function useInventoryMutation<TVars, TResult>(fn: (v: TVars) => Promise<TResult>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () => Promise.all([qc.invalidateQueries({ queryKey: KEY }), qc.invalidateQueries({ queryKey: ["shopping"] })]),
  });
}

export const useAddItem = () => useInventoryMutation((v: AddItem) => api<Item>("/inventory/items", { body: v }));
export const useAddItems = () => useInventoryMutation((items: AddItem[]) => api<Item[]>("/inventory/items/batch", { body: { items } }));
export const readReceipt = (form: FormData) => api<ReadResult>("/inventory/read/receipt", { body: form });
export const readText = (text: string) => api<ReadResult>("/inventory/read/text", { body: { text } });
export const useUpdateItem = () =>
  useInventoryMutation(({ id, ...v }: UpdateItem & { id: number }) => api<Item>(`/inventory/items/${id}`, { method: "PUT", body: v }));
export const useConsume = () =>
  useInventoryMutation(({ id, ...v }: { id: number; quantity?: number; wasted?: boolean }) =>
    api<ItemAction>(`/inventory/items/${id}/consume`, { body: v }));
export const useMove = () =>
  useInventoryMutation(({ id, ...v }: { id: number; locationId: number; adjustExpiry?: boolean }) =>
    api<ItemAction>(`/inventory/items/${id}/move`, { body: v }));

/**
 * Shows "<message> · Undo" after a use, throw-out or move. Undo talks to the query client directly
 * rather than a mutation hook, because the sheet that did the action has usually closed by then.
 */
export function useUndoToast() {
  const qc = useQueryClient();
  const show = useToast();
  return (action: ItemAction, message: string) => show({
    message,
    action: {
      label: "Undo",
      onClick: async () => {
        await api<Item>(`/inventory/events/${action.eventId}/undo`, { body: {} });
        await Promise.all([qc.invalidateQueries({ queryKey: KEY }), qc.invalidateQueries({ queryKey: ["shopping"] })]);
      },
    },
  });
}

export const useWasteStats = (months = 6) =>
  useQuery({ queryKey: [...KEY, "waste", months], queryFn: () => api<WasteStats>(`/inventory/stats/waste?months=${months}`) });
export const useDeleteItem = () => useInventoryMutation((id: number) => api(`/inventory/items/${id}`, { method: "DELETE" }));

export const useSaveLocation = () =>
  useInventoryMutation(({ id, ...v }: { id?: number; name: string; kind: LocationKind }) =>
    api<Location>(id ? `/inventory/locations/${id}` : "/inventory/locations", { method: id ? "PUT" : "POST", body: v }));
export const useDeleteLocation = () => useInventoryMutation((id: number) => api(`/inventory/locations/${id}`, { method: "DELETE" }));
