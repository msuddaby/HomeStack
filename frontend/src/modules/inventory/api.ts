import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/core/api";

export type LocationKind = "pantry" | "fridge" | "freezer" | "other";
export interface Location { id: number; name: string; kind: LocationKind; sort: number }
export interface Category {
  id: string; label: string; emoji: string; defaultLocation: LocationKind;
  pantryDays: number | null; fridgeDays: number | null; freezerDays: number | null; thawedDays: number; freezable: boolean;
}
export interface Product {
  id: number; name: string; brand: string | null; category: string; barcode: string | null; defaultUnit: string | null;
  imageUrl: string | null; pantryDays: number | null; fridgeDays: number | null; freezerDays: number | null; minQuantity: number | null;
}
export interface Item {
  id: number; quantity: number; unit: string | null; purchasedAt: string | null; expiresAt: string | null;
  openedAt: string | null; frozenAt: string | null; notes: string | null; createdAt: string; product: Product; location: Location;
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
}
export type UpdateItem = Omit<Required<AddItem>, "productId" | "product" | "noExpiry">;
export interface BarcodeLookup {
  source: "local" | "openfoodfacts" | "none"; product: Product | null;
  suggestion: { name: string; brand: string | null; category: string; imageUrl: string | null; quantity: string | null } | null;
}

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
export function useProductSearch(q: string) {
  return useQuery({
    queryKey: [...KEY, "products", q],
    queryFn: () => api<Product[]>(`/inventory/products?q=${encodeURIComponent(q)}&limit=8`),
    enabled: q.trim().length > 0,
    placeholderData: (prev) => prev,
  });
}
export const lookupBarcode = (code: string) => api<BarcodeLookup>(`/inventory/barcode/${encodeURIComponent(code)}`);

/** Every inventory mutation refreshes all inventory queries (lists, dashboard, counts). */
function useInventoryMutation<TVars, TResult>(fn: (v: TVars) => Promise<TResult>) {
  const qc = useQueryClient();
  return useMutation({ mutationFn: fn, onSuccess: () => qc.invalidateQueries({ queryKey: KEY }) });
}

export const useAddItem = () => useInventoryMutation((v: AddItem) => api<Item>("/inventory/items", { body: v }));
export const useUpdateItem = () =>
  useInventoryMutation(({ id, ...v }: UpdateItem & { id: number }) => api<Item>(`/inventory/items/${id}`, { method: "PUT", body: v }));
export const useConsume = () =>
  useInventoryMutation(({ id, ...v }: { id: number; quantity?: number; wasted?: boolean }) =>
    api<Item | undefined>(`/inventory/items/${id}/consume`, { body: v }));
export const useMove = () =>
  useInventoryMutation(({ id, ...v }: { id: number; locationId: number; adjustExpiry?: boolean }) =>
    api<Item>(`/inventory/items/${id}/move`, { body: v }));
export const useDeleteItem = () => useInventoryMutation((id: number) => api(`/inventory/items/${id}`, { method: "DELETE" }));

export const useSaveLocation = () =>
  useInventoryMutation(({ id, ...v }: { id?: number; name: string; kind: LocationKind }) =>
    api<Location>(id ? `/inventory/locations/${id}` : "/inventory/locations", { method: id ? "PUT" : "POST", body: v }));
export const useDeleteLocation = () => useInventoryMutation((id: number) => api(`/inventory/locations/${id}`, { method: "DELETE" }));
