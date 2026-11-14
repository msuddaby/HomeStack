import type { WebModule } from "@/core/modules";
import { inventoryModule } from "./inventory";

/** Register front-end modules here (order = nav order). */
export const modules: WebModule[] = [inventoryModule];
