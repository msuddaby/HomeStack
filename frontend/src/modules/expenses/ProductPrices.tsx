import { Link } from "react-router";
import { ChevronRight, Wallet } from "lucide-react";
import { useItems } from "./api";
import { money } from "./format";

/** In the product sheet: "Price history: last $4.49 at Costco", linking to the item in Money. Nothing if never scanned. */
export function ProductPrices({ productId, onNavigate }: { productId: number; onNavigate: () => void }) {
  const item = useItems("").data?.find((i) => i.productId === productId);
  if (!item) return null;
  return (
    <Link to={`/expenses/items/${item.id}`} onClick={onNavigate}
      className="flex items-center gap-3 rounded-xl bg-surface-2 px-3 py-2.5 text-sm">
      <Wallet className="size-4 shrink-0 text-accent" />
      <span className="min-w-0 flex-1">
        <span className="block font-medium">Price history</span>
        {item.lastUnitPrice != null && (
          <span className="block truncate text-xs text-muted">Last {money(item.lastUnitPrice)}{item.lastStore ? ` at ${item.lastStore}` : ""}</span>
        )}
      </span>
      <ChevronRight className="size-4 shrink-0 text-muted" />
    </Link>
  );
}
