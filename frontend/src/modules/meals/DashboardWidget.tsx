import { Link } from "react-router";
import { ChefHat, ChevronRight, CheckCircle2 } from "lucide-react";
import { todayISO } from "@/core/dates";
import { Card, Chip } from "@/core/ui";
import { usePlan, useSuggestions } from "./api";
import { SLOT_LABEL } from "./format";

export function MealsDashboardWidget() {
  const today = todayISO();
  const plan = usePlan(today, today);
  const suggestions = useSuggestions();
  const meals = plan.data ?? [];
  // Prefer ready-to-cook recipes that rescue expiring food.
  const ideas = (suggestions.data ?? []).filter((r) => r.missingCount === 0 && r.usesExpiring.length > 0).slice(0, 3);
  if (!plan.data && !suggestions.data) return null;

  return (
    <Card title={<span className="flex items-center gap-2"><ChefHat className="size-4 text-accent" />Today's meals</span>}
      action={<Link to="/meals" className="text-xs text-accent">Plan →</Link>}>
      {meals.length ? (
        <ul className="flex flex-col gap-1 text-sm">
          {meals.map((m) => (
            <li key={m.id}>
              <Link to={m.recipeId ? `/meals/recipes/${m.recipeId}` : "/meals"} className="flex items-center gap-2 py-1">
                <span className="w-16 shrink-0 text-xs text-muted">{SLOT_LABEL[m.slot]}</span>
                <span className={m.cookedAt ? "flex-1 truncate text-muted line-through" : "flex-1 truncate"}>{m.title}</span>
                {m.cookedAt ? <CheckCircle2 className="size-4 text-accent" /> : m.missingCount > 0 && <Chip tone="warn">need {m.missingCount}</Chip>}
              </Link>
            </li>
          ))}
        </ul>
      ) : <p className="text-sm text-muted">Nothing planned. <Link to="/meals" className="text-accent">Plan something</Link>.</p>}

      {ideas.length > 0 && (
        <div className="mt-3 border-t border-border pt-3">
          <p className="mb-1 text-xs font-medium text-muted">Use it up: you have everything for</p>
          <ul className="flex flex-col">
            {ideas.map((r) => (
              <li key={r.id}>
                <Link to={`/meals/recipes/${r.id}`} className="flex items-center gap-2 py-1 text-sm">
                  <span className="flex-1 truncate">{r.title}</span>
                  <span className="truncate text-xs text-danger">{r.usesExpiring.map((e) => e.name.toLowerCase()).join(", ")}</span>
                  <ChevronRight className="size-4 shrink-0 text-muted" />
                </Link>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Card>
  );
}
