import { NavLink, Outlet } from "react-router";
import { Home, Settings } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { modules } from "@/modules";
import { ToastProvider } from "./toast";
import { cx } from "./ui";

interface NavItem { to: string; label: string; icon: LucideIcon; end?: boolean }

const nav: NavItem[] = [
  { to: "/", label: "Home", icon: Home, end: true },
  ...modules.map((m) => ({ to: `/${m.path}`, label: m.name, icon: m.icon })),
  { to: "/settings", label: "Settings", icon: Settings },
];

export function Layout() {
  return (
    <ToastProvider>
    <div className="min-h-dvh md:flex">
      {/* Desktop sidebar */}
      <aside className="sticky top-0 hidden h-dvh w-56 shrink-0 flex-col gap-1 border-r border-border bg-surface p-3 md:flex">
        <div className="mb-4 flex items-center gap-2 px-2 py-1">
          <img src="/icon.svg" alt="" className="size-8" />
          <span className="text-lg font-bold">HomeStack</span>
        </div>
        {nav.map((n) => (
          <NavLink key={n.to} to={n.to} end={n.end}
            className={({ isActive }) => cx("flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium",
              isActive ? "bg-accent-soft text-accent" : "text-muted hover:bg-surface-2 hover:text-text")}>
            <n.icon className="size-5" />{n.label}
          </NavLink>
        ))}
      </aside>

      <main className="mx-auto w-full max-w-3xl px-4 pb-28 pt-4 md:pb-10 md:pt-8">
        <Outlet />
      </main>

      {/* Phone tab bar */}
      <nav className="pb-safe fixed inset-x-0 bottom-0 z-40 border-t border-border bg-surface/95 backdrop-blur md:hidden">
        <div className="flex">
          {nav.map((n) => (
            <NavLink key={n.to} to={n.to} end={n.end}
              className={({ isActive }) => cx("flex flex-1 flex-col items-center gap-0.5 py-2 text-[11px] font-medium",
                isActive ? "text-accent" : "text-muted")}>
              <n.icon className="size-6" />{n.label}
            </NavLink>
          ))}
        </div>
      </nav>
    </div>
    </ToastProvider>
  );
}

export function PageHeader({ title, subtitle, action }: { title: string; subtitle?: string; action?: React.ReactNode }) {
  return (
    <header className="mb-4 flex items-end justify-between gap-3">
      <div className="min-w-0">
        <h1 className="truncate text-2xl font-bold">{title}</h1>
        {subtitle && <p className="text-sm text-muted">{subtitle}</p>}
      </div>
      {action}
    </header>
  );
}
