import { ChefHat } from "lucide-react";
import type { WebModule } from "@/core/modules";
import { MealsDashboardWidget } from "./DashboardWidget";
import { KitchenPage } from "./KitchenPage";
import { MealsLayout } from "./MealsLayout";
import { PlanPage } from "./PlanPage";
import { RecipeEditPage } from "./RecipeEditPage";
import { RecipePage } from "./RecipePage";
import { RecipesPage } from "./RecipesPage";
import { SuggestPage } from "./SuggestPage";

export const mealsModule: WebModule = {
  id: "meals",
  name: "Meals",
  icon: ChefHat,
  path: "meals",
  routes: [{
    Component: MealsLayout,
    children: [
      { index: true, Component: PlanPage },
      { path: "suggest", Component: SuggestPage },
      { path: "recipes", Component: RecipesPage },
      { path: "recipes/new", Component: RecipeEditPage },
      { path: "recipes/:id", Component: RecipePage },
      { path: "recipes/:id/edit", Component: RecipeEditPage },
    ],
  },
  // Full screen, outside the meals tabs.
  { path: "recipes/:id/kitchen", Component: KitchenPage }],
  DashboardWidget: MealsDashboardWidget,
};
