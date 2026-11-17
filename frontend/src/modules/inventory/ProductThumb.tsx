import { cx } from "@/core/ui";
import type { Product } from "./api";

/** A product's picture, or its category emoji when it has none. Size and rounding come from `className`. */
export function ProductThumb({ product, emoji, className, emojiClassName = "text-xl", lazy = true }: {
  product: Product; emoji?: string; className: string; emojiClassName?: string; lazy?: boolean;
}) {
  return product.image
    ? <img src={product.image} alt="" className={cx("shrink-0 bg-white object-contain", className)} loading={lazy ? "lazy" : undefined} />
    : <span className={cx("flex shrink-0 items-center justify-center bg-surface-2", className, emojiClassName)}>{emoji ?? "📦"}</span>;
}
