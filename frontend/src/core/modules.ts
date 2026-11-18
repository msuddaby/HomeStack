import { createContext, useContext, type ComponentType } from "react";
import type { LucideIcon } from "lucide-react";
import type { RouteObject } from "react-router";

/**
 * Where a card sits on Home. Home is ordered by urgency, not by module:
 * - `actions`: quick buttons at the top (add groceries, scan a receipt)
 * - `attention`: things to deal with now (expired food, an overdue bill, money you owe). Render nothing when there's nothing.
 * - `today`: today's meals, the shopping list
 * - `glance`: how things stand (spending this month, what's in the freezer, waste)
 */
export type HomeSection = "actions" | "attention" | "today" | "glance";

export interface HomeCard {
  id: string;
  section: HomeSection;
  /** Lower comes first within the section. */
  order: number;
  Component: ComponentType;
}

/** Something worth putting on the shopping list, shown on Home's shopping card with a one-tap add. */
export interface RestockSuggestion {
  key: string;
  name: string;
  productId: number | null;
  /** Why: "Expired", "Usually every 2 weeks". */
  reason: string;
}

/**
 * A HomeStack front-end module. Mirrors the backend IModule: the app shell adds a nav entry, mounts `routes` under
 * `/{path}`, puts `homeCards` on Home and `Settings` on the settings page.
 */
export interface WebModule {
  id: string;
  name: string;
  icon: LucideIcon;
  path: string;
  routes: RouteObject[];
  homeCards?: HomeCard[];
  /** Settings card(s). Household-wide: shown under "Household" on the settings page. */
  Settings?: ComponentType;
  /** Shopping suggestions from this module (a hook: called on every render of the shopping card, in module order). */
  useRestockSuggestions?: () => RestockSuggestion[];
  /** Extra rows in the product sheet (e.g. Money's price history), so modules can link to each other's pages. */
  ProductExtra?: ComponentType<{ productId: number; onNavigate: () => void }>;
}

/**
 * The registered modules, provided by the app shell. Modules read each other's extension points through this
 * instead of importing `@/modules` (which would import them back).
 */
export const ModulesContext = createContext<WebModule[]>([]);
export const useModules = () => useContext(ModulesContext);
