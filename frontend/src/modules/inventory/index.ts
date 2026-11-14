import { Refrigerator } from "lucide-react";
import type { WebModule } from "@/core/modules";
import { InventoryDashboardWidget } from "./DashboardWidget";
import { InventoryPage } from "./InventoryPage";
import { LocationSettings } from "./LocationSettings";

export const inventoryModule: WebModule = {
  id: "inventory",
  name: "Inventory",
  icon: Refrigerator,
  path: "inventory",
  routes: [{ index: true, Component: InventoryPage }],
  DashboardWidget: InventoryDashboardWidget,
  Settings: LocationSettings,
};
