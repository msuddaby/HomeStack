import type { ComponentType } from "react";
import type { LucideIcon } from "lucide-react";
import type { RouteObject } from "react-router";

/**
 * A HomeStack front-end module. Mirrors the backend IModule: the app shell adds a nav entry,
 * mounts `routes` under `/{path}`, shows `DashboardWidget` on Home and `Settings` on the settings page.
 */
export interface WebModule {
  id: string;
  name: string;
  icon: LucideIcon;
  path: string;
  routes: RouteObject[];
  DashboardWidget?: ComponentType;
  Settings?: ComponentType;
}
