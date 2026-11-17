import { Link } from "react-router";
import { Wallet } from "lucide-react";
import { Card } from "@/core/ui";
import { useSummary } from "./api";
import { DueList } from "./DueList";
import { monthLabel, money } from "./format";

export function ExpensesDashboardWidget() {
  const summary = useSummary();
  const s = summary.data;
  return (
    <>
      {s && (
        <Card title={<span className="flex items-center gap-2"><Wallet className="size-4 text-accent" />Spending</span>}
          action={<Link to="/expenses" className="text-xs text-accent">Details →</Link>}>
          <p className="text-sm text-muted">{monthLabel(s.month)}</p>
          <p className="text-2xl font-bold tabular-nums">{money(s.total)}</p>
          {s.byCategory.length > 0 && (
            <p className="mt-1 truncate text-xs text-muted">
              {s.byCategory.slice(0, 3).map((b) => `${b.label} ${money(b.total)}`).join(" · ")}
            </p>
          )}
        </Card>
      )}
      <DueList limit={4} />
    </>
  );
}
