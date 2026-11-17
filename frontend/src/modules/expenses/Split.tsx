import { Check } from "lucide-react";
import { cx, Input, Select } from "@/core/ui";
import type { Person, SplitPart } from "./api";
import { money } from "./format";

/**
 * Who paid and who an expense is split with. No one picked = not split (nothing owed).
 * `custom` holds typed amounts per person (rent: $900 / $600); null means an equal split.
 */
export interface SplitValue { paidBy: number | null; people: number[]; custom: Record<number, string> | null }

export const noSplit = (paidBy: number | null): SplitValue => ({ paidBy, people: [], custom: null });

/** What the API stores: weight 1 each for equal, the amounts themselves for custom. Null when not split. */
export function toParts(v: Pick<SplitValue, "people" | "custom">): SplitPart[] | null {
  if (v.people.length === 0) return null;
  return v.people.map((personId) => ({ personId, weight: v.custom ? Math.max(0.01, Number(v.custom[personId]) || 0) : 1 }));
}

export function fromParts(paidBy: number | null, parts: SplitPart[]): SplitValue {
  const equal = parts.every((p) => p.weight === parts[0]?.weight);
  return {
    paidBy, people: parts.map((p) => p.personId),
    custom: equal ? null : Object.fromEntries(parts.map((p) => [p.personId, String(p.weight)])),
  };
}

export const activePeople = (people: Person[]) => people.filter((p) => !p.archived);

/** Tap people in or out. */
export function PeopleChips({ people, selected, onChange, small }: {
  people: Person[]; selected: number[]; onChange: (ids: number[]) => void; small?: boolean;
}) {
  return (
    <div className="flex flex-wrap gap-1.5">
      {activePeople(people).map((p) => {
        const on = selected.includes(p.id);
        return (
          <button key={p.id} type="button" aria-pressed={on}
            onClick={() => onChange(on ? selected.filter((x) => x !== p.id) : [...selected, p.id])}
            className={cx("inline-flex items-center gap-1 rounded-full border font-medium",
              small ? "min-h-7 px-2 text-[11px]" : "min-h-9 px-3 text-xs",
              on ? "border-accent bg-accent-soft text-accent" : "border-border text-muted")}>
            {on && <Check className="size-3" />}{p.isMe ? `${p.name} (me)` : p.name}
          </button>
        );
      })}
    </div>
  );
}

/** "Paid by" + "Split with", with custom amounts when there's a total to share out. */
export function SplitEditor({ people, value, onChange, total }: {
  people: Person[]; value: SplitValue; onChange: (v: SplitValue) => void; total?: number | null;
}) {
  const custom = value.custom;
  const customSum = custom ? value.people.reduce((s, id) => s + (Number(custom[id]) || 0), 0) : 0;
  const each = total != null && value.people.length ? total / value.people.length : null;
  return (
    <div className="flex flex-col gap-2 rounded-2xl bg-surface-2 p-3">
      <label className="flex items-center gap-2 text-sm">
        <span className="w-20 shrink-0 text-muted">Paid by</span>
        <Select value={value.paidBy ?? ""} onChange={(e) => onChange({ ...value, paidBy: e.target.value ? Number(e.target.value) : null })} className="min-h-9">
          {activePeople(people).map((p) => <option key={p.id} value={p.id}>{p.isMe ? `${p.name} (me)` : p.name}</option>)}
        </Select>
      </label>
      <div className="flex items-start gap-2 text-sm">
        <span className="mt-2 w-20 shrink-0 text-muted">Split with</span>
        <div className="min-w-0 flex-1">
          <PeopleChips people={people} selected={value.people} onChange={(ids) => onChange({ ...value, people: ids })} />
          <p className="mt-1 text-xs text-muted">
            {value.people.length === 0 ? "Not split: nobody owes anything."
              : custom ? `Custom amounts${total != null ? `: ${money(customSum)} of ${money(total)}` : ""}.`
              : `Equal${each != null ? `: ${money(each)} each` : ""}. Everyone except whoever paid owes their share.`}
          </p>
        </div>
      </div>
      {total != null && value.people.length > 1 && (
        <label className="flex items-center gap-2 text-xs">
          <input type="checkbox" checked={!!custom} className="size-4 accent-[var(--color-accent)]"
            onChange={(e) => onChange({
              ...value,
              custom: e.target.checked ? Object.fromEntries(value.people.map((id) => [id, (each ?? 0).toFixed(2)])) : null,
            })} />
          Custom amounts
        </label>
      )}
      {custom && value.people.length > 0 && (
        <div className="grid grid-cols-2 gap-2">
          {value.people.map((id) => (
            <label key={id} className="flex items-center gap-2 text-xs">
              <span className="min-w-0 flex-1 truncate">{people.find((p) => p.id === id)?.name}</span>
              <Input inputMode="decimal" value={custom[id] ?? ""} className="min-h-9 w-24 text-right"
                onChange={(e) => onChange({ ...value, custom: { ...custom, [id]: e.target.value } })} />
            </label>
          ))}
          {total != null && Math.abs(customSum - total) >= 0.01 && (
            <p className="col-span-2 text-xs text-warn">Amounts add up to {money(customSum)}, not {money(total)}. Shares are scaled to the total.</p>
          )}
        </div>
      )}
    </div>
  );
}
