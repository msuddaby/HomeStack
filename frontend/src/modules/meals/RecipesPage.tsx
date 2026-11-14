import { useDeferredValue, useMemo, useState } from "react";
import { Link } from "react-router";
import { BookOpen, Link2, Plus, Search } from "lucide-react";
import { Button, cx, Empty, Input, Spinner } from "@/core/ui";
import { useRecipes } from "./api";
import { ImportSheet } from "./ImportSheet";
import { RecipeCard } from "./RecipeCard";

export function RecipesPage() {
  const [q, setQ] = useState("");
  const [tag, setTag] = useState("");
  const [importing, setImporting] = useState(false);
  const recipes = useRecipes(useDeferredValue(q), tag);
  const all = useRecipes();
  const tags = useMemo(() => [...new Set((all.data ?? []).flatMap((r) => r.tags))].sort(), [all.data]);

  return (
    <>
      <div className="mb-3 flex gap-2">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted" />
          <Input className="pl-9" placeholder="Search recipes or ingredients" type="search" value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <Button onClick={() => setImporting(true)} aria-label="Import from link" className="px-3"><Link2 className="size-4" /><span className="hidden sm:inline">Import</span></Button>
        <Link to="/meals/recipes/new"><Button variant="primary" className="px-3" aria-label="New recipe"><Plus className="size-4" /><span className="hidden sm:inline">New</span></Button></Link>
      </div>

      {tags.length > 0 && (
        <div className="-mx-4 mb-3 flex gap-1.5 overflow-x-auto px-4 pb-1">
          {tags.map((t) => (
            <button key={t} onClick={() => setTag(tag === t ? "" : t)}
              className={cx("shrink-0 rounded-full border px-3 py-1 text-xs", tag === t ? "border-accent bg-accent-soft text-accent" : "border-border text-muted")}>
              {t}
            </button>
          ))}
        </div>
      )}

      {recipes.isPending ? <Spinner /> : recipes.data?.length ? (
        <div className="grid gap-2 sm:grid-cols-2">{recipes.data.map((r) => <RecipeCard key={r.id} r={r} />)}</div>
      ) : (
        <Empty icon={<BookOpen className="size-10" />} title={q || tag ? "No matching recipes" : "No recipes yet"}>
          {!q && !tag && (
            <div className="mt-3 flex justify-center gap-2">
              <Button onClick={() => setImporting(true)}><Link2 className="size-4" />Import from a link</Button>
              <Link to="/meals/recipes/new"><Button variant="primary"><Plus className="size-4" />Write one</Button></Link>
            </div>
          )}
        </Empty>
      )}
      <ImportSheet open={importing} onClose={() => setImporting(false)} />
    </>
  );
}
