import { useState } from "react";
import { Pencil, Trash2, UserPlus } from "lucide-react";
import { Button, Card, ErrorText, Input } from "@/core/ui";
import { useAddPerson, useArchivePerson, usePeople, useRenamePerson } from "./api";

/** Settings: people to split expenses with who aren't on HomeStack. (Household members are included automatically.) */
export function PeopleSettings() {
  const people = usePeople();
  const add = useAddPerson();
  const rename = useRenamePerson();
  const archive = useArchivePerson();
  const [name, setName] = useState("");
  const [editing, setEditing] = useState<{ id: number; name: string } | null>(null);
  const outside = (people.data ?? []).filter((p) => p.userId == null && !p.archived);

  return (
    <Card title="Splitting expenses">
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
      {(add.error || rename.error || archive.error) && <div className="mt-2"><ErrorText error={add.error ?? rename.error ?? archive.error} /></div>}
      <p className="mt-2 text-xs text-muted">Removing someone hides them from new splits; what they owe stays on the Owed tab.</p>
    </Card>
  );
}
