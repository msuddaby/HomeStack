import { Link } from "react-router";
import { Sparkles, BookOpen } from "lucide-react";
import { Button, Empty, ErrorText, Spinner } from "@/core/ui";
import { useSuggestions } from "./api";
import { RecipeCard } from "./RecipeCard";

/** Ranked by what you have, nudging toward recipes that use up food about to expire. */
export function SuggestPage() {
  const s = useSuggestions();
  if (s.isPending) return <Spinner />;
  if (s.error) return <ErrorText error={s.error} />;
  if (!s.data.length) return (
    <Empty icon={<BookOpen className="size-10" />} title="Add some recipes first">
      <Link to="/meals/recipes"><Button variant="primary" className="mt-3">Go to recipes</Button></Link>
    </Empty>
  );
  const ready = s.data.filter((r) => r.missingCount === 0);
  const close = s.data.filter((r) => r.missingCount > 0 && r.missingCount <= 2);
  const rest = s.data.filter((r) => r.missingCount > 2);

  return (
    <div className="flex flex-col gap-5">
      {ready.length > 0 ? (
        <section>
          <h2 className="mb-2 flex items-center gap-2 text-sm font-semibold"><Sparkles className="size-4 text-accent" />You can make these now</h2>
          <div className="grid gap-2 sm:grid-cols-2">{ready.map((r) => <RecipeCard key={r.id} r={r} />)}</div>
        </section>
      ) : <p className="text-sm text-muted">Nothing you can make with exactly what's in stock. These are closest.</p>}
      {close.length > 0 && (
        <section>
          <h2 className="mb-2 text-sm font-semibold">A quick shop away</h2>
          <div className="grid gap-2 sm:grid-cols-2">{close.map((r) => <RecipeCard key={r.id} r={r} showMissing />)}</div>
        </section>
      )}
      {rest.length > 0 && (
        <section>
          <h2 className="mb-2 text-sm font-semibold text-muted">Needs more shopping</h2>
          <div className="grid gap-2 sm:grid-cols-2">{rest.map((r) => <RecipeCard key={r.id} r={r} showMissing />)}</div>
        </section>
      )}
    </div>
  );
}
