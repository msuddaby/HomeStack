import { Link } from "react-router";
import { Plus } from "lucide-react";
import { TabbedSection } from "@/core/layout";
import { Button } from "@/core/ui";

const tabs = [
  { to: "/inventory", label: "Items", end: true },
  { to: "/inventory/products", label: "Products" },
  { to: "/inventory/waste", label: "Waste" },
];

export function InventoryLayout() {
  // Phones add with the floating + on the Items tab.
  return <TabbedSection title="Inventory" tabs={tabs} action={
    <Link to="/inventory?add=1" className="hidden md:block"><Button variant="primary"><Plus className="size-4" />Add</Button></Link>
  } />;
}
