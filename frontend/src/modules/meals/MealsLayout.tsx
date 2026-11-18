import { TabbedSection } from "@/core/layout";

const tabs = [
  { to: "/meals", label: "Plan", end: true },
  { to: "/meals/recipes", label: "Recipes" },
  { to: "/meals/suggest", label: "What can I make?" },
];

export function MealsLayout() {
  return <TabbedSection title="Meals" tabs={tabs} />;
}
