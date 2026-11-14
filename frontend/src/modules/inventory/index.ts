import { Refrigerator } from "lucide-react";
import type { WebModule } from "@/core/modules";
import { InventoryDashboardWidget } from "./DashboardWidget";
import { InventoryPage } from "./InventoryPage";
import { LocationSettings } from "./LocationSettings";
import { ProductsPage } from "./ProductsPage";

export const inventoryModule: WebModule = {
  id: "inventory",
  name: "Inventory",
  icon: Refrigerator,
  path: "inventory",
  routes: [{ index: true, Component: InventoryPage }, { path: "products", Component: ProductsPage }],
  DashboardWidget: InventoryDashboardWidget,
  Settings: LocationSettings,
};
