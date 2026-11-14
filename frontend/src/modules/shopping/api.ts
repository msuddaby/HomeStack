import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/core/api";
import type { Item, Product } from "@/modules/inventory/api";

export interface ShoppingItem {
  id: number; name: string; productId: number | null; category: string; quantity: number | null; unit: string | null;
  note: string | null; source: "manual" | "lowStock"; createdAt: string; checkedAt: string | null; checkedBy: string | null;
  product: Product | null;
}
export interface AddShoppingItem { name?: string; productId?: number; category?: string | null; quantity?: number | null; unit?: string | null; note?: string | null }
export interface PutAwayLine {
  shoppingItemId: number; addToInventory: boolean; locationId?: number | null; quantity?: number | null; unit?: string | null;
  expiresAt?: string | null; noExpiry?: boolean; frozenAt?: string | null;
}

const KEY = ["shopping"] as const;
const LIST = [...KEY, "items"] as const;

/** Polls so everyone in the household sees changes within a few seconds while shopping together. */
export const useShoppingList = (poll = false) =>
  useQuery({ queryKey: LIST, queryFn: () => api<ShoppingItem[]>("/shopping/items"), refetchInterval: poll ? 5000 : false });

function useShoppingMutation<TVars, TResult>(fn: (v: TVars) => Promise<TResult>, alsoInventory = false) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSettled: () => Promise.all([
      qc.invalidateQueries({ queryKey: KEY }),
      alsoInventory && qc.invalidateQueries({ queryKey: ["inventory"] }),
    ]),
  });
}

export const useAddToList = () => useShoppingMutation((v: AddShoppingItem) => api<ShoppingItem>("/shopping/items", { body: v }));
export const useUpdateListItem = () =>
  useShoppingMutation(({ id, ...v }: { id: number; name: string; category: string; quantity: number | null; unit: string | null; note: string | null }) =>
    api<ShoppingItem>(`/shopping/items/${id}`, { method: "PUT", body: v }));
export const useRemoveListItem = () => useShoppingMutation((id: number) => api(`/shopping/items/${id}`, { method: "DELETE" }));
export const useClearChecked = () => useShoppingMutation(() => api("/shopping/clear-checked", { method: "POST", body: {} }));
export const usePutAway = () =>
  useShoppingMutation((lines: PutAwayLine[]) => api<{ addedToInventory: number; cleared: number; items: Item[] }>("/shopping/put-away", { body: { lines } }), true);

/** Checking off is optimistic: in a store with bad signal it must feel instant. */
export function useCheck() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, checked }: { id: number; checked: boolean }) => api<ShoppingItem>(`/shopping/items/${id}/check`, { body: { checked } }),
    onMutate: async ({ id, checked }) => {
      await qc.cancelQueries({ queryKey: LIST });
      const prev = qc.getQueryData<ShoppingItem[]>(LIST);
      qc.setQueryData<ShoppingItem[]>(LIST, (items) =>
        items?.map((i) => (i.id === id ? { ...i, checkedAt: checked ? new Date().toISOString() : null } : i)));
      return { prev };
    },
    onError: (_e, _v, ctx) => ctx?.prev && qc.setQueryData(LIST, ctx.prev),
    onSettled: () => qc.invalidateQueries({ queryKey: LIST }),
  });
}
