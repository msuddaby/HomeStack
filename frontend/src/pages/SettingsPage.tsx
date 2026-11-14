import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { LogOut, UserPlus } from "lucide-react";
import { api, ApiError } from "@/core/api";
import { useLogout, useMe } from "@/core/auth";
import { PageHeader } from "@/core/layout";
import { Button, Card, ErrorText, Field, Input } from "@/core/ui";
import { modules } from "@/modules";

interface UserDto { id: number; name: string; username: string }

export function SettingsPage() {
  const me = useMe();
  const logout = useLogout();
  return (
    <>
      <PageHeader title="Settings" subtitle={me.data?.household.name} />
      <div className="flex flex-col gap-4">
        <HouseholdMembers />
        {modules.map((m) => m.Settings && <m.Settings key={m.id} />)}
        <Card title="Account">
          <p className="mb-3 text-sm text-muted">Signed in as <b className="text-text">{me.data?.user.username}</b></p>
          <Button onClick={() => logout.mutate()} busy={logout.isPending}><LogOut className="size-4" />Sign out</Button>
        </Card>
      </div>
    </>
  );
}

function HouseholdMembers() {
  const qc = useQueryClient();
  const users = useQuery({ queryKey: ["household-users"], queryFn: () => api<UserDto[]>("/household/users") });
  const [adding, setAdding] = useState(false);
  const [f, setF] = useState({ name: "", username: "", password: "" });
  const create = useMutation({
    mutationFn: () => api<UserDto>("/household/users", { body: f }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["household-users"] }); setAdding(false); setF({ name: "", username: "", password: "" }); },
  });
  const err = create.error instanceof ApiError ? create.error : undefined;

  return (
    <Card title="Household members" action={!adding && <Button variant="ghost" onClick={() => setAdding(true)}><UserPlus className="size-4" />Add</Button>}>
      <ul className="divide-y divide-border">
        {users.data?.map((u) => (
          <li key={u.id} className="flex items-center gap-3 py-2">
            <span className="flex size-9 items-center justify-center rounded-full bg-accent-soft font-semibold text-accent">{u.name[0]?.toUpperCase()}</span>
            <div><p className="text-sm font-medium">{u.name}</p><p className="text-xs text-muted">@{u.username}</p></div>
          </li>
        ))}
      </ul>
      {adding && (
        <form className="mt-3 grid gap-3 border-t border-border pt-3" onSubmit={(e) => { e.preventDefault(); create.mutate(); }}>
          <Field label="Name" error={err?.field("name")}><Input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} required /></Field>
          <Field label="Username" error={err?.field("username")}><Input autoCapitalize="none" value={f.username} onChange={(e) => setF({ ...f, username: e.target.value })} required /></Field>
          <Field label="Password" error={err?.field("password")} hint="At least 8 characters. They can sign in with this on their own phone.">
            <Input type="password" autoComplete="new-password" value={f.password} onChange={(e) => setF({ ...f, password: e.target.value })} required minLength={8} />
          </Field>
          {create.error && !err?.errors && <ErrorText error={create.error} />}
          <div className="flex gap-2">
            <Button variant="primary" busy={create.isPending}>Add member</Button>
            <Button type="button" variant="ghost" onClick={() => setAdding(false)}>Cancel</Button>
          </div>
        </form>
      )}
    </Card>
  );
}
