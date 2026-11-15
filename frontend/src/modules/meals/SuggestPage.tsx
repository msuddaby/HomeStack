import { Link } from "react-router";
import { Sparkles, BookOpen } from "lucide-react";
import { Button, Empty, ErrorText, Spinner } from "@/core/ui";
import { useSuggestions, type RecipeSummary } from "./api";
import { IdeasPanel } from "./Ideas";
import { RecipeCard } from "./RecipeCard";

/** AI ideas on top, then saved recipes ranked by what you have, nudging toward ones that use up food about to expire. */
export function SuggestPage() {
  return (
    <div className="flex flex-col gap-5">
      {/* Kept outside the list below so its ideas survive the list refreshing after one is saved. */}
      <IdeasPanel />
      <SavedSuggestions />
    </div>
  );
}

function SavedSuggestions() {
  const s = useSuggestions();
  if (s.isPending) return <Spinner />;
  if (s.error) return <ErrorText error={s.error} />;
  if (!s.data.length) return (
    <Empty icon={<BookOpen className="size-10" />} title="No saved recipes yet">
      <Link to="/meals/recipes"><Button variant="primary" className="mt-3">Go to recipes</Button></Link>
    </Empty>
  );
  const ready = s.data.filter((r) => r.missingCount === 0);
  const close = s.data.filter((r) => r.missingCount > 0 && r.missingCount <= 2);
  const rest = s.data.filter((r) => r.missingCount > 2);

  return (
    <>
      {ready.length > 0 ? <Group title={<><Sparkles className="size-4 text-accent" />You can make these now</>} recipes={ready} />
        : <p className="text-sm text-muted">No saved recipe works with exactly what's in stock. These are closest.</p>}
      {close.length > 0 && <Group title="A quick shop away" recipes={close} showMissing />}
      {rest.length > 0 && <Group title={<span className="text-muted">Needs more shopping</span>} recipes={rest} showMissing />}
    </>
  );
}

function Group({ title, recipes, showMissing }: { title: React.ReactNode; recipes: RecipeSummary[]; showMissing?: boolean }) {
  return (
    <section>
      <h2 className="mb-2 flex items-center gap-2 text-sm font-semibold">{title}</h2>
      <div className="grid gap-2 sm:grid-cols-2">{recipes.map((r) => <RecipeCard key={r.id} r={r} showMissing={showMissing} />)}</div>
    </section>
  );
}
