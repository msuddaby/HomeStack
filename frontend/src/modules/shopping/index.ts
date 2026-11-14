import { ShoppingCart } from "lucide-react";
import type { WebModule } from "@/core/modules";
import { ShoppingDashboardWidget } from "./DashboardWidget";
import { ShoppingPage } from "./ShoppingPage";

export const shoppingModule: WebModule = {
  id: "shopping",
  name: "Shopping",
  icon: ShoppingCart,
  path: "shopping",
  routes: [{ index: true, Component: ShoppingPage }],
  DashboardWidget: ShoppingDashboardWidget,
};
