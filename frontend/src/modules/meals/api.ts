import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/core/api";

export type MealSlot = "breakfast" | "lunch" | "dinner" | "snack";
export const SLOTS: MealSlot[] = ["breakfast", "lunch", "dinner", "snack"];

export interface Ingredient {
  id: number; text: string; name: string; quantity: number | null; unit: string | null; note: string | null; optional: boolean;
  productId: number | null; productName: string | null; linkLocked: boolean; staple: boolean;
  inStock: boolean; onHand: number | null; onHandUnit: string | null; soonestExpiry: string | null;
}
export interface Recipe {
  id: number; title: string; description: string | null; servings: number; prepMinutes: number | null; cookMinutes: number | null;
  sourceUrl: string | null; imageUrl: string | null; steps: string[]; tags: string[]; notes: string | null;
  ingredients: Ingredient[]; missingCount: number; updatedAt: string;
}
export interface RecipeSummary {
  id: number; title: string; imageUrl: string | null; tags: string[]; totalMinutes: number | null; servings: number;
  ingredientCount: number; missingCount: number; missing: string[]; usesExpiring: { name: string; expiresAt: string }[];
}
export interface RecipeInput {
  title: string; description: string | null; servings: number; prepMinutes: number | null; cookMinutes: number | null;
  sourceUrl: string | null; imageUrl: string | null; ingredients: string[]; steps: string[]; tags: string[]; notes: string | null;
}
export interface PlanEntry {
  id: number; date: string; slot: MealSlot; recipeId: number | null; title: string; imageUrl: string | null;
  servings: number | null; note: string | null; cookedAt: string | null; missingCount: number;
}
export interface PlanInput { date: string; slot: MealSlot; recipeId?: number | null; title?: string | null; servings?: number | null; note?: string | null }
export interface CookPlan {
  recipeId: number; title: string; servings: number;
  lines: {
    ingredientId: number; text: string; needed: number | null; neededUnit: string | null; productId: number | null; productName: string | null;
    candidates: { itemId: number; quantity: number; unit: string | null; location: string; expiresAt: string | null }[];
    suggestedItemId: number | null; suggestedQuantity: number | null;
  }[];
}

/** An AI meal idea: a saved recipe (recipeId) or a new one (draft). Missing/expiring are checked against real stock. */
export interface Idea {
  recipeId: number | null; title: string; why: string | null; description: string | null; servings: number; totalMinutes: number | null;
  ingredientCount: number; missing: string[]; usesExpiring: { name: string; expiresAt: string }[]; draft: RecipeInput | null; date: string | null;
}

const KEY = ["meals"] as const;

export const useRecipes = (q = "", tag = "") =>
  useQuery({
    queryKey: [...KEY, "recipes", q, tag],
    queryFn: () => api<RecipeSummary[]>(`/meals/recipes?${new URLSearchParams({ q, tag })}`),
    placeholderData: (prev) => prev,
  });
export const useSuggestions = () => useQuery({ queryKey: [...KEY, "suggestions"], queryFn: () => api<RecipeSummary[]>("/meals/suggestions") });
export const useRecipe = (id: number | null) =>
  useQuery({ queryKey: [...KEY, "recipe", id], queryFn: () => api<Recipe>(`/meals/recipes/${id}`), enabled: id != null });
export const usePlan = (from: string, to: string) =>
  useQuery({ queryKey: [...KEY, "plan", from, to], queryFn: () => api<PlanEntry[]>(`/meals/plan?from=${from}&to=${to}`), placeholderData: (prev) => prev });
export const fetchCookPlan = (id: number, servings?: number) =>
  api<CookPlan>(`/meals/recipes/${id}/cook${servings ? `?servings=${servings}` : ""}`);
export const importRecipe = (url: string) => api<RecipeInput>("/meals/import", { body: { url } });

/** Meals data depends on inventory (availability) and feeds shopping, so refresh all three. */
function useMealsMutation<TVars, TResult>(fn: (v: TVars) => Promise<TResult>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () => Promise.all(["meals", "inventory", "shopping"].map((k) => qc.invalidateQueries({ queryKey: [k] }))),
  });
}

export const useSaveRecipe = () =>
  useMealsMutation(({ id, ...v }: RecipeInput & { id?: number }) =>
    api<Recipe>(id ? `/meals/recipes/${id}` : "/meals/recipes", { method: id ? "PUT" : "POST", body: v }));
export const useDeleteRecipe = () => useMealsMutation((id: number) => api(`/meals/recipes/${id}`, { method: "DELETE" }));
export const useLinkIngredient = () =>
  useMealsMutation(({ recipeId, ingredientId, productId, staple }: { recipeId: number; ingredientId: number; productId: number | null; staple: boolean }) =>
    api<Recipe>(`/meals/recipes/${recipeId}/ingredients/${ingredientId}/link`, { method: "PUT", body: { productId, staple } }));
export const useCook = () =>
  useMealsMutation((v: { recipeId: number; planEntryId?: number | null; uses: { itemId: number; quantity: number | null }[] }) =>
    api<{ itemsUpdated: number }>("/meals/cook", { body: v }));
export const useRecipeToList = () =>
  useMealsMutation((id: number) => api<{ added: number; names: string[] }>(`/meals/recipes/${id}/shopping`, { method: "POST", body: {} }));
export const usePlanToList = () =>
  useMealsMutation((v: { from: string; to: string }) => api<{ added: number; names: string[] }>("/meals/plan/shopping", { body: v }));
export const useSavePlan = () =>
  useMealsMutation(({ id, ...v }: PlanInput & { id?: number }) =>
    api<PlanEntry>(id ? `/meals/plan/${id}` : "/meals/plan", { method: id ? "PUT" : "POST", body: v }));
export const useDeletePlan = () => useMealsMutation((id: number) => api(`/meals/plan/${id}`, { method: "DELETE" }));

export const fetchIdeas = (v: { count?: number; wish?: string | null; week?: boolean }) => api<{ ideas: Idea[] }>("/meals/ideas", { body: v });
export const useAcceptIdea = () =>
  useMealsMutation((v: { recipeId?: number | null; draft?: RecipeInput | null; date?: string | null; slot?: MealSlot }) =>
    api<{ recipeId: number; planEntryId: number | null }>("/meals/ideas/accept", { body: v }));
