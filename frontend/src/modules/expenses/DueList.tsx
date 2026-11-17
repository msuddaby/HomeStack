import { Link } from "react-router";
import { Check, Plus, RotateCw } from "lucide-react";
import { Button, Card } from "@/core/ui";
import { useAddToList, useShoppingList } from "@/modules/shopping/api";
import { useDueItems } from "./api";
import { rhythmText } from "./format";

/** "Usually buy about now", with a one-tap add to the shopping list. Hidden when nothing's due. */
export function DueList({ limit = 6 }: { limit?: number }) {
  const due = useDueItems();
  const list = useShoppingList();
  const add = useAddToList();
  const items = (due.data ?? []).slice(0, limit);
  if (!items.length) return null;
  const onList = new Set((list.data ?? []).filter((i) => !i.checkedAt).map((i) => i.name.toLowerCase()));

  return (
    <Card title={<span className="flex items-center gap-2"><RotateCw className="size-4 text-accent" />Usually buy about now</span>}>
      <ul className="flex flex-col">
        {items.map((i) => {
          const listed = onList.has(i.name.toLowerCase()) || (list.data ?? []).some((x) => !x.checkedAt && i.productId != null && x.productId === i.productId);
          return (
            <li key={i.id} className="flex items-center gap-2 py-1.5">
              <Link to={`/expenses/items/${i.id}`} className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium">{i.name}</p>
                <p className="truncate text-xs text-muted">{rhythmText(i.rhythm!)}</p>
              </Link>
              {listed ? (
                <span className="flex items-center gap-1 text-xs text-accent"><Check className="size-3.5" />On list</span>
              ) : (
                <Button variant="ghost" className="min-h-9 px-2 text-xs" busy={add.isPending && add.variables?.name === i.name}
                  onClick={() => add.mutate(i.productId != null ? { productId: i.productId, name: i.name } : { name: i.name })}>
                  <Plus className="size-4" />List
                </Button>
              )}
            </li>
          );
        })}
      </ul>
    </Card>
  );
}
