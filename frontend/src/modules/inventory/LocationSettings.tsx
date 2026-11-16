import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { Button, Card, ErrorText, Input, Select } from "@/core/ui";
import { useDeleteLocation, useLocations, useSaveLocation, type Location, type LocationKind } from "./api";
import { KIND_LABEL } from "./logic";

const KINDS = Object.keys(KIND_LABEL) as LocationKind[];

export function LocationSettings() {
  const locations = useLocations().data ?? [];
  const save = useSaveLocation();
  const del = useDeleteLocation();
  const [name, setName] = useState("");
  const [kind, setKind] = useState<LocationKind>("freezer");

  return (
    <Card title="Storage locations">
      <p className="-mt-1 mb-3 text-xs text-muted">Add places like a garage chest freezer or a basement pantry. The type sets the shelf-life rules.</p>
      <ul className="flex flex-col gap-2">
        {locations.map((l) => <LocationRow key={l.id} loc={l} onSave={(v) => save.mutate({ id: l.id, ...v })} onDelete={() => del.mutate(l.id)} />)}
      </ul>
      <form className="mt-3 flex gap-2 border-t border-border pt-3" onSubmit={async (e) => {
        e.preventDefault();
        await save.mutateAsync({ name, kind });
        setName("");
      }}>
        <Input className="min-w-0 flex-1" placeholder="New location" value={name} onChange={(e) => setName(e.target.value)} required />
        <Select value={kind} onChange={(e) => setKind(e.target.value as LocationKind)} className="w-28 shrink-0">
          {KINDS.map((k) => <option key={k} value={k}>{KIND_LABEL[k]}</option>)}
        </Select>
        <Button className="shrink-0 px-3" busy={save.isPending} aria-label="Add location"><Plus className="size-4" /></Button>
      </form>
      <div className="mt-2"><ErrorText error={save.error ?? del.error} /></div>
    </Card>
  );
}

function LocationRow({ loc, onSave, onDelete }: { loc: Location; onSave: (v: { name: string; kind: LocationKind }) => void; onDelete: () => void }) {
  const [name, setName] = useState(loc.name);
  return (
    <li className="flex gap-2">
      <Input className="min-w-0 flex-1" value={name} onChange={(e) => setName(e.target.value)} onBlur={() => name.trim() && name !== loc.name && onSave({ name, kind: loc.kind })} />
      <Select value={loc.kind} onChange={(e) => onSave({ name, kind: e.target.value as LocationKind })} className="w-28 shrink-0">
        {KINDS.map((k) => <option key={k} value={k}>{KIND_LABEL[k]}</option>)}
      </Select>
      <Button variant="ghost" className="shrink-0 px-3" onClick={onDelete} aria-label={`Delete ${loc.name}`}><Trash2 className="size-4" /></Button>
    </li>
  );
}
