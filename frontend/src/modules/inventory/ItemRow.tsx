import { MotionItem } from "@/core/motion";
import { Chip } from "@/core/ui";
import type { Category, Item } from "./api";
import { ProductThumb } from "./ProductThumb";
import { formatQty, itemStatus } from "./logic";

/** One inventory item in a list. Rows draw their own dividers, so lists shouldn't add `divide-y`. */
export function ItemRow({ item, category, showLocation, onClick, action, leaving, entering }: {
  item: Item; category?: Category; showLocation?: boolean; onClick?: () => void; action?: React.ReactNode;
  /** From useExitingList: fold away / open up. */
  leaving?: boolean; entering?: boolean;
}) {
  const status = itemStatus(item);
  const qty = formatQty(item.quantity, item.unit);
  return (
    <MotionItem as="li" leaving={leaving} entering={entering} className="first:*:*:border-t-0">
    <div className="flex items-center gap-2 border-t border-border">
      <button onClick={onClick} className="flex min-w-0 flex-1 items-center gap-3 rounded-xl py-2 text-left transition-colors hover:bg-surface-2 active:bg-surface-2 sm:px-2">
        <ProductThumb product={item.product} emoji={category?.emoji} className="size-10 rounded-lg" />
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
    </div>
    </MotionItem>
  );
}
