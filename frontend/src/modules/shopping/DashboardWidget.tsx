import { Link } from "react-router";
import { ShoppingCart, ChevronRight } from "lucide-react";
import { Card } from "@/core/ui";
import { useShoppingList } from "./api";

export function ShoppingDashboardWidget() {
  const list = useShoppingList();
  const todo = (list.data ?? []).filter((i) => !i.checkedAt);
  if (!list.data) return null;
  return (
    <Link to="/shopping" className="block">
      <Card className="hover:bg-surface-2">
        <div className="flex items-center gap-3">
          <span className="flex size-10 items-center justify-center rounded-xl bg-accent-soft text-accent"><ShoppingCart className="size-5" /></span>
          <div className="min-w-0 flex-1">
            <p className="font-semibold">{todo.length ? `${todo.length} on the shopping list` : "Shopping list is empty"}</p>
            {todo.length > 0 && <p className="truncate text-sm text-muted">{todo.slice(0, 5).map((i) => i.name).join(", ")}{todo.length > 5 ? "…" : ""}</p>}
          </div>
          <ChevronRight className="size-5 text-muted" />
        </div>
      </Card>
    </Link>
  );
}
