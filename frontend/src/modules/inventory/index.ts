import { Refrigerator } from "lucide-react";
import type { WebModule } from "@/core/modules";
import { InventoryDashboardWidget } from "./DashboardWidget";
import { InventoryPage } from "./InventoryPage";
import { LocationSettings } from "./LocationSettings";
import { ProductsPage } from "./ProductsPage";
import { WastePage } from "./WastePage";

export const inventoryModule: WebModule = {
  id: "inventory",
  name: "Inventory",
  icon: Refrigerator,
  path: "inventory",
  routes: [{ index: true, Component: InventoryPage }, { path: "products", Component: ProductsPage }, { path: "waste", Component: WastePage }],
  DashboardWidget: InventoryDashboardWidget,
  Settings: LocationSettings,
};
