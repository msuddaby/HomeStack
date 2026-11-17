import { Wallet } from "lucide-react";
import type { WebModule } from "@/core/modules";
import { BillsPage } from "./BillsPage";
import { ExpensesDashboardWidget } from "./DashboardWidget";
import { ExpensesLayout } from "./ExpensesLayout";
import { ItemPage } from "./ItemPage";
import { ItemsPage } from "./ItemsPage";
import { OverviewPage } from "./OverviewPage";
import { OwedPage } from "./OwedPage";
import { PeopleSettings } from "./PeopleSettings";
import { ReceiptPage } from "./ReceiptPage";
import { ReceiptsPage } from "./ReceiptsPage";

export const expensesModule: WebModule = {
  id: "expenses",
  name: "Spending",
  icon: Wallet,
  path: "expenses",
  routes: [{
    Component: ExpensesLayout,
    children: [
      { index: true, Component: OverviewPage },
      { path: "receipts", Component: ReceiptsPage },
      { path: "receipts/:id", Component: ReceiptPage },
      { path: "bills", Component: BillsPage },
      { path: "owed", Component: OwedPage },
      { path: "items", Component: ItemsPage },
      { path: "items/:id", Component: ItemPage },
    ],
  }],
  DashboardWidget: ExpensesDashboardWidget,
  Settings: PeopleSettings,
};
