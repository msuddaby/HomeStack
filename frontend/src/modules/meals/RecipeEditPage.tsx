import { useEffect, useState } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/core/api";
import { Button, ErrorText, Field, Input, Spinner } from "@/core/ui";
import { useRecipe, useSaveRecipe, type RecipeInput } from "./api";

const empty: RecipeInput = { title: "", description: null, servings: 4, prepMinutes: null, cookMinutes: null, sourceUrl: null, imageUrl: null, ingredients: [], steps: [], tags: [], notes: null };

interface Form { title: string; description: string; servings: string; prepMinutes: string; cookMinutes: string; sourceUrl: string; imageUrl: string; ingredients: string; steps: string; tags: string; notes: string }

const toForm = (r: RecipeInput): Form => ({
  title: r.title, description: r.description ?? "", servings: String(r.servings), prepMinutes: r.prepMinutes?.toString() ?? "",
  cookMinutes: r.cookMinutes?.toString() ?? "", sourceUrl: r.sourceUrl ?? "", imageUrl: r.imageUrl ?? "",
  ingredients: r.ingredients.join("\n"), steps: r.steps.join("\n\n"), tags: r.tags.join(", "), notes: r.notes ?? "",
});

const lines = (s: string) => s.split("\n").map((l) => l.trim()).filter(Boolean);
const num = (s: string) => (s.trim() ? Number(s) : null);

export function RecipeEditPage() {
  const { id } = useParams();
  const recipeId = id && id !== "new" ? Number(id) : null;
  const existing = useRecipe(recipeId);
  const draft = (useLocation().state as { draft?: RecipeInput } | null)?.draft;
  const save = useSaveRecipe();
  const navigate = useNavigate();
  const [f, setF] = useState<Form | null>(recipeId ? null : toForm(draft ?? empty));

  useEffect(() => {
    if (existing.data) setF(toForm({ ...existing.data, ingredients: existing.data.ingredients.map((i) => i.text) }));
  }, [existing.data]);

  if (!f) return <Spinner />;
  const set = (k: keyof Form) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setF({ ...f, [k]: e.target.value });
  const err = save.error instanceof ApiError ? save.error : undefined;
  const area = "w-full rounded-xl border border-border bg-surface px-3 py-2 text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent/25";

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const saved = await save.mutateAsync({
      id: recipeId ?? undefined, title: f.title, description: f.description || null, servings: Number(f.servings) || 4,
      prepMinutes: num(f.prepMinutes), cookMinutes: num(f.cookMinutes), sourceUrl: f.sourceUrl || null, imageUrl: f.imageUrl || null,
      ingredients: lines(f.ingredients),
      // Steps are separated by blank lines or single newlines; either works.
      steps: lines(f.steps),
      tags: f.tags.split(",").map((t) => t.trim()).filter(Boolean),
      notes: f.notes || null,
    });
    navigate(`/meals/recipes/${saved.id}`, { replace: true });
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <Link to={recipeId ? `/meals/recipes/${recipeId}` : "/meals/recipes"} className="inline-flex items-center gap-1 text-sm text-muted"><ArrowLeft className="size-4" />Back</Link>
      <h1 className="text-2xl font-bold">{recipeId ? "Edit recipe" : draft ? "Review imported recipe" : "New recipe"}</h1>
      {draft && <p className="rounded-xl bg-accent-soft px-3 py-2 text-sm text-accent">Imported. Check the ingredients and steps, then save.</p>}

      <Field label="Title" error={err?.field("title")}><Input value={f.title} onChange={set("title")} required /></Field>
      <div className="grid grid-cols-3 gap-3">
        <Field label="Serves"><Input inputMode="numeric" value={f.servings} onChange={set("servings")} /></Field>
        <Field label="Prep (min)"><Input inputMode="numeric" value={f.prepMinutes} onChange={set("prepMinutes")} /></Field>
        <Field label="Cook (min)"><Input inputMode="numeric" value={f.cookMinutes} onChange={set("cookMinutes")} /></Field>
      </div>
      <Field label="Ingredients" hint="One per line, as you'd write it: 2 cups flour, 3 eggs, 1 onion, diced" error={err?.field("ingredients")}>
        <textarea className={area} rows={Math.max(6, lines(f.ingredients).length + 1)} value={f.ingredients} onChange={set("ingredients")} placeholder={"2 cups flour\n3 large eggs\n1 onion, diced"} />
      </Field>
      <Field label="Steps" hint='One step per line. Start a line with "## " for a section heading.' error={err?.field("steps")}>
        <textarea className={area} rows={8} value={f.steps} onChange={set("steps")} />
      </Field>
      <Field label="Tags" hint="Comma separated, e.g. dinner, quick, vegetarian"><Input value={f.tags} onChange={set("tags")} /></Field>
      <Field label="Description"><Input value={f.description} onChange={set("description")} /></Field>
      <Field label="Notes"><textarea className={area} rows={3} value={f.notes} onChange={set("notes")} /></Field>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Source link"><Input type="url" value={f.sourceUrl} onChange={set("sourceUrl")} /></Field>
        <Field label="Photo link"><Input type="url" value={f.imageUrl} onChange={set("imageUrl")} /></Field>
      </div>
      {save.error && <ErrorText error={save.error} />}
      <div className="flex gap-2 pb-4">
        <Button variant="primary" className="flex-1" busy={save.isPending}>Save recipe</Button>
      </div>
    </form>
  );
}
