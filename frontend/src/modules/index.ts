import type { WebModule } from "@/core/modules";
import { expensesModule } from "./expenses";
import { inventoryModule } from "./inventory";
import { mealsModule } from "./meals";
import { shoppingModule } from "./shopping";

/**
 * Register front-end modules here (order = nav order).
 * A module may import another module's public `api.ts` (e.g. shopping uses inventory products),
 * mirroring how backend modules depend on each other's services.
 */
export const modules: WebModule[] = [inventoryModule, shoppingModule, mealsModule, expensesModule];
