import { useDeferredValue, useMemo, useState } from "react";
import { Link } from "react-router";
import { ArrowLeft, Search } from "lucide-react";
import { PageHeader } from "@/core/layout";
import { Chip, Empty, Input, Spinner } from "@/core/ui";
import { useCategories, useProducts } from "./api";
import { ProductSheet } from "./ProductSheet";
import { ProductThumb } from "./ProductThumb";

/** Everything you've ever added, so you can set minimums and shelf lives even when it's out of stock. */
export function ProductsPage() {
  const [q, setQ] = useState("");
  const query = useDeferredValue(q);
  const products = useProducts(query);
  const categories = useCategories().data ?? [];
  const catById = useMemo(() => new Map(categories.map((c) => [c.id, c])), [categories]);
  const [editing, setEditing] = useState<number | null>(null);

  return (
    <>
      <Link to="/inventory" className="mb-2 inline-flex items-center gap-1 text-sm text-muted"><ArrowLeft className="size-4" />Inventory</Link>
      <PageHeader title="Products" subtitle="Set minimums and shelf lives" />
      <div className="relative mb-3">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted" />
        <Input className="pl-9" placeholder="Search products" value={q} onChange={(e) => setQ(e.target.value)} type="search" />
      </div>
      {products.isPending ? <Spinner /> : products.data?.length ? (
        <ul className="divide-y divide-border">
          {products.data.map((p) => (
            <li key={p.id}>
              <button onClick={() => setEditing(p.id)} className="flex w-full items-center gap-3 py-2.5 text-left hover:bg-surface-2 sm:px-2">
                <ProductThumb product={p} emoji={catById.get(p.category)?.emoji} className="size-9 rounded-lg" emojiClassName="text-lg" />
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium">{p.name}</p>
                  <p className="truncate text-xs text-muted">{[p.brand, catById.get(p.category)?.label].filter(Boolean).join(" · ")}</p>
                </div>
                {p.minQuantity != null && <Chip tone="accent">Keep {p.minQuantity}{p.defaultUnit ? ` ${p.defaultUnit}` : ""}</Chip>}
              </button>
            </li>
          ))}
        </ul>
      ) : <Empty title="No products yet">Products are created when you add items.</Empty>}
      <ProductSheet productId={editing} onClose={() => setEditing(null)} />
    </>
  );
}
