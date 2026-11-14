import { useDeferredValue, useEffect, useState } from "react";
import { Check, Search } from "lucide-react";
import { Button, cx, ErrorText, Input, Sheet } from "@/core/ui";
import { useProducts } from "@/modules/inventory/api";
import { useLinkIngredient, type Ingredient } from "./api";

/** Choose which inventory product an ingredient means, or mark it as always on hand. */
export function LinkSheet({ recipeId, ingredient, onClose }: { recipeId: number; ingredient: Ingredient | null; onClose: () => void }) {
  const [q, setQ] = useState("");
  const products = useProducts(useDeferredValue(q)).data ?? [];
  const link = useLinkIngredient();
  useEffect(() => { setQ(""); link.reset(); }, [ingredient?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const choose = async (productId: number | null, staple = false) => {
    await link.mutateAsync({ recipeId, ingredientId: ingredient!.id, productId, staple });
    onClose();
  };

  return (
    <Sheet open={!!ingredient} onClose={onClose} title={ingredient?.text ?? ""}>
      <p className="mb-3 text-sm text-muted">Which item in your inventory is this? HomeStack uses it to check whether you have it and to deduct it when you cook.</p>
      <div className="mb-3 grid grid-cols-2 gap-2">
        <Button className={cx(ingredient?.staple && "ring-2 ring-accent")} busy={link.isPending} onClick={() => choose(null, true)}>Always have it</Button>
        <Button className={cx(ingredient?.linkLocked && !ingredient.productId && !ingredient.staple && "ring-2 ring-accent")} busy={link.isPending} onClick={() => choose(null)}>Don't track</Button>
      </div>
      <div className="relative mb-2">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted" />
        <Input className="pl-9" placeholder="Search products" value={q} onChange={(e) => setQ(e.target.value)} type="search" />
      </div>
      <ul className="divide-y divide-border">
        {products.map((p) => (
          <li key={p.id}>
            <button onClick={() => choose(p.id)} className="flex w-full items-center justify-between py-2.5 text-left hover:bg-surface-2">
              <span>{p.name}{p.brand && <span className="ml-1 text-xs text-muted">{p.brand}</span>}</span>
              {ingredient?.productId === p.id && <Check className="size-4 text-accent" />}
            </button>
          </li>
        ))}
      </ul>
      {products.length === 0 && <p className="py-4 text-center text-sm text-muted">No products match. Products are created when you add items to inventory.</p>}
      <ErrorText error={link.error} />
    </Sheet>
  );
}
