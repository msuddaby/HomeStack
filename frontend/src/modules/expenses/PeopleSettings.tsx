import { useState } from "react";
import { ChevronDown, GitMerge, Pencil, Trash2, UserPlus } from "lucide-react";
import { useMe } from "@/core/auth";
import { Button, Card, ErrorText, Field, Input, Select, Sheet } from "@/core/ui";
import { useAddPerson, useArchivePerson, useMergePerson, useNotSamePerson, usePeople, useRenamePerson, type Person } from "./api";

/** "Cole (removed member)", "Pat (not on HomeStack)": which of two people with one name this is. */
export const personLabel = (p: Person) => p.userId != null ? p.name : `${p.name} (${p.formerMember ? "removed member" : "not on HomeStack"})`;

/** Settings: people to split expenses with who aren't on HomeStack. (Household members are included automatically.) */
export function PeopleSettings() {
  const people = usePeople();
  const me = useMe();
  const isAdmin = !!me.data?.user.isAdmin;
  const add = useAddPerson();
  const rename = useRenamePerson();
  const archive = useArchivePerson();
  const notSame = useNotSamePerson();
  const [name, setName] = useState("");
  const [editing, setEditing] = useState<{ id: number; name: string } | null>(null);
  const [merging, setMerging] = useState<{ from: Person; into: number | null } | null>(null);
  const [merged, setMerged] = useState<string | null>(null);
  const [showRemoved, setShowRemoved] = useState(false);
  const all = people.data ?? [];
  const outside = all.filter((p) => p.userId == null && !p.archived);
  const removed = all.filter((p) => p.archived);
  const suggestions = isAdmin ? all.filter((p) => p.sameAsId != null) : [];
  const byId = (id: number | null) => all.find((p) => p.id === id);
  const error = add.error ?? rename.error ?? archive.error ?? notSame.error;

  return (
    <Card title="Splitting expenses">
      {suggestions.map((p) => (
        <div key={p.id} className="mb-3 rounded-2xl border border-border bg-surface-2 p-3 text-sm">
          <p>
            <b>{personLabel(p)}</b> may be the same person as <b>{byId(p.sameAsId)?.name}</b>. Merge them to move everything
            {p.formerMember ? " from before they were removed" : ""} (what they paid and owe) to {byId(p.sameAsId)?.name}.
          </p>
          <div className="mt-2 flex gap-2">
            <Button variant="primary" onClick={() => { setMerged(null); setMerging({ from: p, into: p.sameAsId }); }}><GitMerge className="size-4" />Merge…</Button>
            <Button variant="ghost" busy={notSame.isPending && notSame.variables === p.id} onClick={() => notSame.mutate(p.id)}>Not the same</Button>
          </div>
        </div>
      ))}
      {merged && <p className="mb-3 text-sm text-muted">{merged}</p>}
      <p className="mb-3 text-sm text-muted">
        Household members are always available to split with. Add anyone else here (a roommate or friend who isn't on HomeStack) by name.
      </p>
      {outside.length > 0 && (
        <ul className="mb-3 divide-y divide-border">
          {outside.map((p) => (
            <li key={p.id} className="flex items-center gap-2 py-2">
              {editing?.id === p.id ? (
                <form className="flex flex-1 gap-2" onSubmit={async (e) => { e.preventDefault(); await rename.mutateAsync(editing); setEditing(null); }}>
                  <Input value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} autoFocus required />
                  <Button variant="primary" busy={rename.isPending}>Save</Button>
                </form>
              ) : (
                <>
                  <span className="min-w-0 flex-1 truncate text-sm">{p.name}</span>
                  <Button variant="ghost" className="px-3" aria-label={`Rename ${p.name}`} onClick={() => setEditing({ id: p.id, name: p.name })}><Pencil className="size-4" /></Button>
                  {isAdmin && (
                    <Button variant="ghost" className="px-3" aria-label={`Merge ${p.name} into someone`}
                      onClick={() => { setMerged(null); setMerging({ from: p, into: p.sameAsId }); }}><GitMerge className="size-4" /></Button>
                  )}
                  <Button variant="ghost" className="px-3" aria-label={`Remove ${p.name}`} busy={archive.isPending && archive.variables === p.id}
                    onClick={() => archive.mutate(p.id)}><Trash2 className="size-4" /></Button>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
      <form className="flex gap-2" onSubmit={async (e) => { e.preventDefault(); await add.mutateAsync(name.trim()); setName(""); }}>
        <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Name" maxLength={80} />
        <Button busy={add.isPending} disabled={!name.trim()}><UserPlus className="size-4" />Add</Button>
      </form>
      {error && <div className="mt-2"><ErrorText error={error} /></div>}
      <p className="mt-2 text-xs text-muted">Removing someone hides them from new splits; what they owe stays on the Owed tab.</p>

      {isAdmin && removed.length > 0 && (
        <div className="mt-3 border-t border-border pt-2">
          <button className="flex w-full items-center justify-between py-2 text-sm text-muted" onClick={() => setShowRemoved(!showRemoved)}>
            Removed people ({removed.length})
            <ChevronDown className={`size-4 transition-transform ${showRemoved ? "rotate-180" : ""}`} />
          </button>
          {showRemoved && (
            <ul className="divide-y divide-border">
              {removed.map((p) => (
                <li key={p.id} className="flex items-center gap-2 py-2">
                  <span className="min-w-0 flex-1 truncate text-sm">{personLabel(p)}</span>
                  <Button variant="ghost" onClick={() => { setMerged(null); setMerging({ from: p, into: p.sameAsId }); }}><GitMerge className="size-4" />Merge…</Button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {merging && (
        <MergeSheet from={merging.from} initial={merging.into} people={all} onClose={() => setMerging(null)}
          onMerged={(text) => { setMerging(null); setMerged(text); }} />
      )}
    </Card>
  );
}

function MergeSheet({ from, initial, people, onClose, onMerged }: {
  from: Person; initial: number | null; people: Person[]; onClose: () => void; onMerged: (text: string) => void;
}) {
  const merge = useMergePerson();
  const choices = people.filter((p) => !p.archived && p.id !== from.id);
  const [into, setInto] = useState<number | null>(initial ?? null);
  const target = choices.find((p) => p.id === into);

  return (
    <Sheet open onClose={onClose} title={`Merge ${from.name}`}
      footer={
        <Button variant="primary" className="w-full" disabled={!target} busy={merge.isPending}
          onClick={async () => {
            const r = await merge.mutateAsync({ id: from.id, intoPersonId: target!.id });
            onMerged(`Merged ${from.name} into ${r.into}: ${r.expenses} ${r.expenses === 1 ? "expense" : "expenses"} moved.`);
          }}>
          <GitMerge className="size-4" />{target ? `Merge into ${target.name}` : "Merge"}
        </Button>
      }>
      <div className="flex flex-col gap-3 text-sm">
        <Field label="Same person as">
          <Select value={into ?? ""} onChange={(e) => setInto(e.target.value ? Number(e.target.value) : null)}>
            <option value="">Choose…</option>
            {choices.map((p) => <option key={p.id} value={p.id}>{personLabel(p)}</option>)}
          </Select>
        </Field>
        <p className="text-muted">
          Everything of {personLabel(from)}'s moves to {target?.name ?? "them"}: expenses they paid, their part of splits, what they owe
          and are owed, and recurring payments. Then {from.name} is removed. This can't be undone.
        </p>
        {merge.error && <ErrorText error={merge.error} />}
      </div>
    </Sheet>
  );
}
