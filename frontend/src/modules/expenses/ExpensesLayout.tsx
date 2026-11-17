import { useState } from "react";
import { NavLink, Outlet } from "react-router";
import { Plus, Receipt } from "lucide-react";
import { PageHeader } from "@/core/layout";
import { Button, cx } from "@/core/ui";
import { ManualExpenseSheet } from "./ManualExpenseSheet";
import { ScanReceiptSheet } from "./ScanReceiptSheet";

const tabs = [
  { to: "/expenses", label: "Overview", end: true },
  { to: "/expenses/receipts", label: "Receipts" },
  { to: "/expenses/items", label: "Items" },
];

export function ExpensesLayout() {
  const [scanning, setScanning] = useState(false);
  const [adding, setAdding] = useState(false);
  return (
    <>
      <PageHeader title="Spending" action={
        <div className="flex gap-2">
          <Button variant="ghost" className="px-3" onClick={() => setAdding(true)} aria-label="Add an expense"><Plus className="size-4" /><span className="hidden sm:inline">Expense</span></Button>
          <Button variant="primary" onClick={() => setScanning(true)}><Receipt className="size-4" />Scan</Button>
        </div>
      } />
      <nav className="-mx-4 mb-4 flex gap-1 overflow-x-auto border-b border-border px-4">
        {tabs.map((t) => (
          <NavLink key={t.to} to={t.to} end={t.end}
            className={({ isActive }) => cx("shrink-0 border-b-2 px-3 py-2.5 text-sm font-medium",
              isActive ? "border-accent text-accent" : "border-transparent text-muted hover:text-text")}>
            {t.label}
          </NavLink>
        ))}
      </nav>
      <Outlet />
      <ScanReceiptSheet open={scanning} onClose={() => setScanning(false)} />
      <ManualExpenseSheet open={adding} onClose={() => setAdding(false)} />
    </>
  );
}
