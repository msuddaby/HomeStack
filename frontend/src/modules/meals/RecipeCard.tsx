import { Link } from "react-router";
import { Clock, CheckCircle2 } from "lucide-react";
import { Chip } from "@/core/ui";
import type { RecipeSummary } from "./api";
import { minutes } from "./format";

export function RecipeCard({ r, showMissing }: { r: RecipeSummary; showMissing?: boolean }) {
  return (
    <Link to={`/meals/recipes/${r.id}`} className="press flex min-w-0 gap-3 rounded-2xl border border-border bg-surface p-2.5 transition-colors hover:bg-surface-2 active:bg-surface-2">
      {r.image
        ? <img src={r.image} alt="" loading="lazy" className="size-20 shrink-0 rounded-xl object-cover" />
        : <span className="flex size-20 shrink-0 items-center justify-center rounded-xl bg-surface-2 text-3xl">🍽️</span>}
      <div className="flex min-w-0 flex-1 flex-col justify-center gap-1">
        <p className="line-clamp-2 font-semibold leading-snug">{r.title}</p>
        <div className="flex flex-wrap items-center gap-1 text-xs text-muted">
          {r.missingCount === 0
            ? <Chip tone="accent"><CheckCircle2 className="size-3" />Have everything</Chip>
            : <Chip tone={r.missingCount <= 2 ? "warn" : "neutral"}>Missing {r.missingCount}</Chip>}
          {r.usesExpiring.length > 0 && <Chip tone="danger">Uses {r.usesExpiring.map((e) => e.name.toLowerCase()).join(", ")}</Chip>}
          {minutes(r.totalMinutes) && <span className="inline-flex items-center gap-1"><Clock className="size-3" />{minutes(r.totalMinutes)}</span>}
        </div>
        {showMissing && r.missingCount > 0 && <p className="truncate text-xs text-muted">Need: {r.missing.join(", ")}</p>}
      </div>
    </Link>
  );
}
