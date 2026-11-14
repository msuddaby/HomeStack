import { NavLink, Outlet } from "react-router";
import { cx } from "@/core/ui";

const tabs = [
  { to: "/meals", label: "Plan", end: true },
  { to: "/meals/recipes", label: "Recipes" },
  { to: "/meals/suggest", label: "What can I make?" },
];

export function MealsLayout() {
  return (
    <>
      <nav className="-mx-4 mb-4 flex gap-1 overflow-x-auto border-b border-border px-4">
        {tabs.map((t) => (
          <NavLink key={t.to} to={t.to} end={t.end}
            className={({ isActive }) => cx("shrink-0 border-b-2 px-3 py-2.5 text-sm font-medium",
              isActive ? "border-accent text-accent" : "border-transparent text-muted hover:text-text")}>
            {t.label}
          </NavLink>
        ))}
      </nav>
      <Outlet />
    </>
  );
}
