import { Chip } from "@/core/ui";
import type { Category, Item } from "./api";
import { formatQty, itemStatus } from "./logic";

export function ItemRow({ item, category, showLocation, onClick, action }: {
  item: Item; category?: Category; showLocation?: boolean; onClick?: () => void; action?: React.ReactNode;
}) {
  const status = itemStatus(item);
  const qty = formatQty(item.quantity, item.unit);
  return (
    <li className="flex items-center gap-2">
      <button onClick={onClick} className="flex min-w-0 flex-1 items-center gap-3 rounded-xl py-2 text-left hover:bg-surface-2 sm:px-2">
        {item.product.imageUrl
          ? <img src={item.product.imageUrl} alt="" className="size-10 shrink-0 rounded-lg bg-white object-contain" loading="lazy" />
          : <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-surface-2 text-xl">{category?.emoji ?? "📦"}</span>}
        <div className="min-w-0 flex-1">
          <p className="truncate font-medium">
            {item.product.name}
            {qty && <span className="ml-1.5 text-sm font-normal text-muted">{qty}</span>}
          </p>
          <div className="mt-0.5 flex flex-wrap items-center gap-1">
            {status && <Chip tone={status.tone}>{status.label}</Chip>}
            {showLocation && <Chip>{item.location.name}</Chip>}
            {item.openedAt && <Chip>Opened</Chip>}
          </div>
        </div>
      </button>
      {action}
    </li>
  );
}
