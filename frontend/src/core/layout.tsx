import { NavLink, Outlet, useLocation } from "react-router";
import { Home, Settings } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { modules } from "@/modules";
import { ModulesContext } from "./modules";
import { ToastProvider } from "./toast";
import { cx } from "./ui";
import { haptic } from "./motion";

interface NavItem { to: string; label: string; icon: LucideIcon; end?: boolean }

const nav: NavItem[] = [
  { to: "/", label: "Home", icon: Home, end: true },
  ...modules.map((m) => ({ to: `/${m.path}`, label: m.name, icon: m.icon })),
];
// Phones get five tabs; Settings is the gear on Home. The desktop sidebar has room for it.
const sideNav: NavItem[] = [...nav, { to: "/settings", label: "Settings", icon: Settings }];

export function Layout() {
  return (
    <ModulesContext.Provider value={modules}>
    <ToastProvider>
    <div className="min-h-dvh md:flex">
      {/* Desktop sidebar */}
      <aside className="vt-static-side sticky top-0 hidden h-dvh w-56 shrink-0 flex-col gap-1 border-r border-border bg-surface p-3 md:flex">
        <div className="mb-4 flex items-center gap-2 px-2 py-1">
          <img src="/icon.svg" alt="" className="size-8" />
          <span className="text-lg font-bold">HomeStack</span>
        </div>
        {sideNav.map((n) => (
          <NavLink key={n.to} to={n.to} end={n.end} viewTransition
            className={({ isActive }) => cx("press flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium",
              isActive ? "bg-accent-soft text-accent" : "text-muted hover:bg-surface-2 hover:text-text")}>
            <n.icon className="size-5" />{n.label}
          </NavLink>
        ))}
      </aside>

      <main className="mx-auto w-full max-w-3xl px-4 pb-28 pt-4 md:pb-10 md:pt-8">
        <Outlet />
      </main>

      {/* Phone tab bar */}
      <nav className="vt-static pb-safe fixed inset-x-0 bottom-0 z-40 border-t border-border bg-surface/95 backdrop-blur md:hidden">
        <div className="flex">
          {nav.map((n) => (
            <NavLink key={n.to} to={n.to} end={n.end} viewTransition onClick={() => haptic(5)}
              className={({ isActive }) => cx("press flex flex-1 flex-col items-center gap-0.5 py-2 text-[11px] font-medium transition-colors",
                isActive ? "text-accent" : "text-muted")}>
              {({ isActive }) => (
                <>
                  <span className={cx("grid h-7 w-12 place-items-center rounded-full transition-[background-color,transform] duration-300",
                    isActive ? "scale-100 bg-accent-soft" : "scale-90 bg-transparent")}>
                    <n.icon className="size-6" />
                  </span>
                  {n.label}
                </>
              )}
            </NavLink>
          ))}
        </div>
      </nav>
    </div>
    </ToastProvider>
    </ModulesContext.Provider>
  );
}

/**
 * The one page layout: a title, then (optionally) a row of tabs. Every section uses it, so pages look and work
 * the same everywhere. `action` is the page's main button.
 */
export function PageHeader({ title, subtitle, action }: { title: string; subtitle?: string; action?: React.ReactNode }) {
  return (
    <header className="mb-3 flex items-end justify-between gap-3">
      <div className="min-w-0">
        <h1 className="truncate text-2xl font-bold">{title}</h1>
        {subtitle && <p className="text-sm text-muted">{subtitle}</p>}
      </div>
      {action}
    </header>
  );
}

export interface Tab { to: string; label: string; end?: boolean }

/** Tabs under a section's title (Plan · Recipes, Overview · Receipts…). Scrolls sideways if they don't fit. */
export function TabNav({ tabs }: { tabs: Tab[] }) {
  return (
    <nav className="-mx-4 mb-4 flex gap-1 overflow-x-auto border-b border-border px-4">
      {tabs.map((t) => (
        <NavLink key={t.to} to={t.to} end={t.end}
          className={({ isActive }) => cx("press shrink-0 border-b-2 px-3 py-2.5 text-sm font-medium transition-colors duration-200",
            isActive ? "border-accent text-accent" : "border-transparent text-muted hover:text-text")}>
          {t.label}
        </NavLink>
      ))}
    </nav>
  );
}

/** A section's tabs and the page under them; the page fades in when you switch tabs. */
export function TabbedSection({ title, action, tabs }: { title: string; action?: React.ReactNode; tabs: Tab[] }) {
  const { pathname } = useLocation();
  return (
    <>
      <PageHeader title={title} action={action} />
      <TabNav tabs={tabs} />
      <div key={pathname} className="animate-fade-in"><Outlet /></div>
    </>
  );
}
