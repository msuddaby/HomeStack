import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { ArrowLeft, CalendarPlus, ChefHat, Clock, ExternalLink, Minus, Pencil, Plus, ShoppingCart, Trash2, Users } from "lucide-react";
import { shortDate } from "@/core/dates";
import { Button, Chip, cx, Empty, ErrorText, Spinner } from "@/core/ui";
import { useDeleteRecipe, useRecipe, useRecipeToList, type Ingredient } from "./api";
import { AddMealSheet } from "./AddMealSheet";
import { CookSheet } from "./CookSheet";
import { minutes, prettyAmount } from "./format";
import { LinkSheet } from "./LinkSheet";

export function RecipePage() {
  const id = Number(useParams().id);
  const recipe = useRecipe(id);
  const [servings, setServings] = useState<number | null>(null);
  const [linking, setLinking] = useState<Ingredient | null>(null);
  const [cooking, setCooking] = useState(false);
  const [planning, setPlanning] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const toList = useRecipeToList();
  const del = useDeleteRecipe();
  const navigate = useNavigate();

  if (recipe.isPending) return <Spinner />;
  if (recipe.error) return <Empty title="Recipe not found"><Link to="/meals/recipes" className="text-accent">Back to recipes</Link></Empty>;
  const r = recipe.data;
  const serves = servings ?? r.servings;
  const factor = serves / r.servings;

  return (
    <article className="flex flex-col gap-5">
      <Link to="/meals/recipes" className="inline-flex items-center gap-1 text-sm text-muted"><ArrowLeft className="size-4" />Recipes</Link>
      {r.imageUrl && <img src={r.imageUrl} alt="" className="aspect-[16/9] w-full rounded-3xl object-cover" />}
      <header>
        <h1 className="text-2xl font-bold leading-tight">{r.title}</h1>
        {r.description && <p className="mt-1 text-sm text-muted">{r.description}</p>}
        <div className="mt-2 flex flex-wrap items-center gap-2 text-sm text-muted">
          {minutes((r.prepMinutes ?? 0) + (r.cookMinutes ?? 0)) && <span className="inline-flex items-center gap-1"><Clock className="size-4" />{minutes((r.prepMinutes ?? 0) + (r.cookMinutes ?? 0))}</span>}
          {r.tags.map((t) => <Chip key={t}>{t}</Chip>)}
          {r.sourceUrl && <a href={r.sourceUrl} target="_blank" rel="noreferrer noopener" className="inline-flex items-center gap-1 text-accent"><ExternalLink className="size-3.5" />Source</a>}
        </div>
      </header>

      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Button variant="primary" onClick={() => setCooking(true)}><ChefHat className="size-4" />Cook</Button>
        <Button onClick={() => setPlanning(true)}><CalendarPlus className="size-4" />Plan</Button>
        <Button busy={toList.isPending} disabled={r.missingCount === 0 || toList.isSuccess} onClick={() => toList.mutate(r.id)}>
          <ShoppingCart className="size-4" />{toList.isSuccess ? `Added ${toList.data.added}` : `Missing ${r.missingCount}`}
        </Button>
        <Link to={`/meals/recipes/${r.id}/edit`} className="contents"><Button variant="ghost"><Pencil className="size-4" />Edit</Button></Link>
      </div>
      <ErrorText error={toList.error} />

      <section>
        <div className="mb-2 flex items-center justify-between">
          <h2 className="text-lg font-semibold">Ingredients</h2>
          <div className="flex items-center gap-1 text-sm">
            <Users className="size-4 text-muted" />
            <Button variant="ghost" className="min-h-8 px-2" onClick={() => setServings(Math.max(1, serves - 1))} aria-label="Fewer servings"><Minus className="size-3.5" /></Button>
            <span className="w-6 text-center font-semibold tabular-nums">{serves}</span>
            <Button variant="ghost" className="min-h-8 px-2" onClick={() => setServings(serves + 1)} aria-label="More servings"><Plus className="size-3.5" /></Button>
          </div>
        </div>
        <ul className="divide-y divide-border">
          {r.ingredients.map((i) => <IngredientRow key={i.id} i={i} factor={factor} onClick={() => setLinking(i)} />)}
        </ul>
        <p className="mt-2 text-xs text-muted">Tap an ingredient to change which inventory item it uses.</p>
      </section>

      {r.steps.length > 0 && (
        <section>
          <h2 className="mb-2 text-lg font-semibold">Method</h2>
          <ol className="flex flex-col gap-3">
            {(() => { let n = 0; return r.steps.map((s, idx) => s.startsWith("## ")
              ? <li key={idx} className="pt-2 text-sm font-semibold uppercase tracking-wide text-muted">{s.slice(3)}</li>
              : <li key={idx} className="flex gap-3"><span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-accent-soft text-sm font-semibold text-accent">{++n}</span><p className="pt-0.5 leading-relaxed">{s}</p></li>); })()}
          </ol>
        </section>
      )}

      {r.notes && <section><h2 className="mb-1 text-lg font-semibold">Notes</h2><p className="whitespace-pre-line text-sm">{r.notes}</p></section>}

      <div className="pb-4">
        {confirmDelete ? (
          <div className="flex items-center gap-2">
            <span className="text-sm">Delete this recipe? Planned meals are kept.</span>
            <Button variant="danger" busy={del.isPending} onClick={async () => { await del.mutateAsync(r.id); navigate("/meals/recipes", { replace: true }); }}>Delete</Button>
            <Button variant="ghost" onClick={() => setConfirmDelete(false)}>Cancel</Button>
          </div>
        ) : (
          <button className="inline-flex items-center gap-1 text-xs text-muted hover:text-danger" onClick={() => setConfirmDelete(true)}><Trash2 className="size-3" />Delete recipe</button>
        )}
      </div>

      <LinkSheet recipeId={r.id} ingredient={linking} onClose={() => setLinking(null)} />
      <CookSheet recipeId={r.id} servings={serves} open={cooking} onClose={() => setCooking(false)} />
      <AddMealSheet open={planning} onClose={() => setPlanning(false)} recipe={{ id: r.id, title: r.title }} />
    </article>
  );
}

function IngredientRow({ i, factor, onClick }: { i: Ingredient; factor: number; onClick: () => void }) {
  const amount = i.quantity != null ? `${prettyAmount(i.quantity * factor)}${i.unit ? ` ${i.unit}` : ""}` : null;
  const status = i.staple ? { tone: "neutral" as const, label: "always have" }
    : i.inStock ? { tone: "accent" as const, label: i.soonestExpiry ? `have · ${shortDate(i.soonestExpiry)}` : "have" }
    : i.optional ? { tone: "neutral" as const, label: "optional" }
    : { tone: "warn" as const, label: i.productId || i.linkLocked ? "need" : "need · not linked" };
  return (
    <li>
      <button onClick={onClick} className="flex w-full items-start gap-3 py-2.5 text-left hover:bg-surface-2 sm:px-2">
        <span className={cx("mt-1.5 size-2.5 shrink-0 rounded-full", i.inStock ? "bg-accent" : i.optional ? "bg-border" : "bg-warn")} />
        <span className="min-w-0 flex-1">
          {amount && <b className="mr-1.5">{amount}</b>}{i.name}
          {i.note && <span className="text-muted">, {i.note}</span>}
          {i.productName && i.productName.toLowerCase() !== i.name.toLowerCase() && <span className="block text-xs text-muted">uses {i.productName}</span>}
        </span>
        <Chip tone={status.tone} className="shrink-0">{status.label}</Chip>
      </button>
    </li>
  );
}
