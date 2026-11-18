import { Refrigerator } from "lucide-react";
import type { WebModule } from "@/core/modules";
import { inventoryHomeCards, useExpiredSuggestions } from "./HomeCards";
import { InventoryLayout } from "./InventoryLayout";
import { InventoryPage } from "./InventoryPage";
import { LocationSettings } from "./LocationSettings";
import { ProductsPage } from "./ProductsPage";
import { WastePage } from "./WastePage";

export const inventoryModule: WebModule = {
  id: "inventory",
  name: "Inventory",
  icon: Refrigerator,
  path: "inventory",
  routes: [{
    Component: InventoryLayout,
    children: [{ index: true, Component: InventoryPage }, { path: "products", Component: ProductsPage }, { path: "waste", Component: WastePage }],
  }],
  homeCards: inventoryHomeCards,
  useRestockSuggestions: useExpiredSuggestions,
  Settings: LocationSettings,
};
