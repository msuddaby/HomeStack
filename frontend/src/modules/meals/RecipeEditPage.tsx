import { useEffect, useState } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router";
import { ArrowLeft, ImagePlus } from "lucide-react";
import { ApiError, errorMessage } from "@/core/api";
import { shrinkImage } from "@/core/ai";
import { useToast } from "@/core/toast";
import { Button, ErrorText, Field, Input, PhotoButton, Spinner } from "@/core/ui";
import { useRecipe, useRemoveRecipeImage, useSaveRecipe, useSetRecipeImage, type RecipeInput } from "./api";

/** Photo changes wait for "Save recipe", like the rest of the form (a new recipe has nothing to attach to until then). */
type PhotoChange = { kind: "none" } | { kind: "new"; blob: Blob; preview: string } | { kind: "remove" };

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
  const setImage = useSetRecipeImage();
  const removeImage = useRemoveRecipeImage();
  const toast = useToast();
  const navigate = useNavigate();
  const [f, setF] = useState<Form | null>(recipeId ? null : toForm(draft ?? empty));
  const [photo, setPhoto] = useState<PhotoChange>({ kind: "none" });
  const [preparing, setPreparing] = useState(false);
  const [photoError, setPhotoError] = useState<string | null>(null);

  // Fill the form once. Coming back from the camera refetches the recipe, which mustn't wipe what's been typed.
  useEffect(() => {
    if (existing.data) setF((prev) => prev ?? toForm({ ...existing.data, ingredients: existing.data.ingredients.map((i) => i.text) }));
  }, [existing.data]);
  // Free the preview's memory when it's replaced or the page closes.
  useEffect(() => () => { if (photo.kind === "new") URL.revokeObjectURL(photo.preview); }, [photo]);

  if (!f) return <Spinner />;

  // What the photo will be after saving. A typed link shows as-is; the saved one shows the server's copy.
  const linkUnchanged = f.imageUrl === (existing.data?.imageUrl ?? "");
  const shown = photo.kind === "new" ? photo.preview : photo.kind === "remove" ? null
    : linkUnchanged && existing.data ? existing.data.image : f.imageUrl || null;

  const pickPhoto = async (file: File) => {
    setPhotoError(null);
    setPreparing(true);
    try {
      // Phones take huge photos; the server keeps a ~1000px copy, so don't upload more than it needs.
      const blob = await shrinkImage(file, 1600, 0.88);
      setPhoto({ kind: "new", blob, preview: URL.createObjectURL(blob) });
      setF((prev) => prev && { ...prev, imageUrl: "" });
    } catch (e) {
      setPhotoError(errorMessage(e));
    } finally {
      setPreparing(false);
    }
  };
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
    // The recipe is saved either way; a failed photo is reported on the recipe page rather than risking a second save.
    try {
      if (photo.kind === "new") await setImage.mutateAsync({ id: saved.id, photo: photo.blob });
      else if (photo.kind === "remove" && saved.image) await removeImage.mutateAsync(saved.id);
    } catch (e) {
      toast({ message: `Recipe saved, but the photo wasn't: ${errorMessage(e)}`, tone: "danger" });
    }
    navigate(`/meals/recipes/${saved.id}`, { replace: true });
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <Link to={recipeId ? `/meals/recipes/${recipeId}` : "/meals/recipes"} className="inline-flex items-center gap-1 text-sm text-muted"><ArrowLeft className="size-4" />Back</Link>
      <h1 className="text-2xl font-bold">{recipeId ? "Edit recipe" : draft ? "Review imported recipe" : "New recipe"}</h1>
      {draft && <p className="rounded-xl bg-accent-soft px-3 py-2 text-sm text-accent">Imported. Check the ingredients and steps, then save.</p>}

      <Field label="Title" error={err?.field("title")}><Input value={f.title} onChange={set("title")} required /></Field>
      <div className="flex items-center gap-3">
        {shown
          ? <img src={shown} alt="" className="aspect-[4/3] w-28 shrink-0 rounded-xl bg-surface-2 object-cover" />
          : <span className="flex aspect-[4/3] w-28 shrink-0 items-center justify-center rounded-xl bg-surface-2 text-3xl">🍽️</span>}
        <div className="flex flex-wrap gap-2">
          <PhotoButton onFile={pickPhoto} busy={preparing}>
            {!preparing && <ImagePlus className="size-4" />}
            {preparing ? "Preparing photo…" : shown ? "Change photo" : "Add photo"}
          </PhotoButton>
          {shown && !preparing && (
            <Button type="button" variant="ghost" onClick={() => { setPhoto({ kind: "remove" }); setF({ ...f, imageUrl: "" }); }}>Remove</Button>
          )}
        </div>
      </div>
      {photoError && <p className="-mt-2 text-sm text-danger">{photoError}</p>}
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
        <Field label="Photo link" hint="Or use Add photo above.">
          <Input type="url" value={f.imageUrl} onChange={(e) => { setPhoto({ kind: "none" }); set("imageUrl")(e); }} />
        </Field>
      </div>
      {save.error && <ErrorText error={save.error} />}
      <div className="flex gap-2 pb-4">
        <Button variant="primary" className="flex-1" busy={save.isPending || setImage.isPending || removeImage.isPending}>Save recipe</Button>
      </div>
    </form>
  );
}
