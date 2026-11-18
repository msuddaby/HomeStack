import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Check, ChevronRight, KeyRound, LogOut, Server, Trash2, UserPlus } from "lucide-react";
import { Link } from "react-router";
import { api, ApiError } from "@/core/api";
import { useLogout, useMe } from "@/core/auth";
import { PageHeader } from "@/core/layout";
import { Button, Card, Chip, ErrorText, Field, Input, Sheet } from "@/core/ui";
import { modules } from "@/modules";
import { AiCard, BackupsCard, NotificationsCard } from "./SystemSettings";

interface UserDto { id: number; name: string; username: string; isAdmin: boolean }

export function SettingsPage() {
  const me = useMe();
  const logout = useLogout();
  const [changingPassword, setChangingPassword] = useState(false);
  return (
    <>
      <PageHeader title="Settings" subtitle={me.data?.household.name} />
      <div className="flex flex-col gap-4">
        {me.data?.isInstanceAdmin && (
          <Link to="/admin" className="flex items-center gap-3 rounded-2xl border border-border bg-surface p-4">
            <Server className="size-5 text-accent" />
            <span className="min-w-0 flex-1">
              <span className="block text-sm font-medium">Manage HomeStack</span>
              <span className="block text-xs text-muted">Households on this server, invites, shared AI</span>
            </span>
            <ChevronRight className="size-4 text-muted" />
          </Link>
        )}
        <HouseholdMembers />
        <AiCard />
        <NotificationsCard />
        {modules.map((m) => m.Settings && <m.Settings key={m.id} />)}
        {/* Backups hold every household on the server. */}
        {me.data?.isInstanceAdmin && <BackupsCard />}
        <Card title="Account">
          <p className="mb-3 text-sm text-muted">Signed in as <b className="text-text">{me.data?.user.username}</b></p>
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => setChangingPassword(true)}><KeyRound className="size-4" />Change password</Button>
            <Button onClick={() => logout.mutate()} busy={logout.isPending}><LogOut className="size-4" />Sign out</Button>
          </div>
        </Card>
        <ChangePasswordSheet open={changingPassword} onClose={() => setChangingPassword(false)} />
        <p className="text-center text-xs text-muted">Version built {new Date(__BUILD_TIME__).toLocaleString()}</p>
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
  const me = useMe();
  const [removing, setRemoving] = useState<UserDto | null>(null);
  const remove = useMutation({
    mutationFn: (id: number) => api(`/household/users/${id}`, { method: "DELETE" }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["household-users"] }); setRemoving(null); },
  });
  // Admins can make others admin (they mark split payments as received in Spending).
  const setAdmin = useMutation({
    mutationFn: ({ id, isAdmin }: { id: number; isAdmin: boolean }) => api<UserDto>(`/household/users/${id}/admin`, { method: "PUT", body: { isAdmin } }),
    onSuccess: () => Promise.all([qc.invalidateQueries({ queryKey: ["household-users"] }), qc.invalidateQueries({ queryKey: ["me"] })]),
  });
  const iAmAdmin = !!users.data?.find((u) => u.id === me.data?.user.id)?.isAdmin;
  const admins = users.data?.filter((u) => u.isAdmin).length ?? 0;

  return (
    <Card title="Household members" action={!adding && <Button variant="ghost" onClick={() => setAdding(true)}><UserPlus className="size-4" />Add</Button>}>
      <ul className="divide-y divide-border">
        {users.data?.map((u) => (
          <li key={u.id} className="flex items-center gap-3 py-2">
            <span className="flex size-9 items-center justify-center rounded-full bg-accent-soft font-semibold text-accent">{u.name[0]?.toUpperCase()}</span>
            <div className="min-w-0 flex-1">
              <p className="flex items-center gap-2 text-sm font-medium">{u.name}{u.isAdmin && <Chip tone="accent">Admin</Chip>}</p>
              <p className="text-xs text-muted">@{u.username}</p>
            </div>
            {/* The household always keeps one admin. */}
            {iAmAdmin && !(u.isAdmin && admins === 1) && (
              <Button variant="ghost" className="min-h-9 px-2 text-xs" busy={setAdmin.isPending && setAdmin.variables?.id === u.id}
                onClick={() => setAdmin.mutate({ id: u.id, isAdmin: !u.isAdmin })}>
                {u.isAdmin ? "Remove admin" : "Make admin"}
              </Button>
            )}
            {u.id !== me.data?.user.id && (
              <Button variant="ghost" className="px-3" aria-label={`Remove ${u.name}`} onClick={() => { remove.reset(); setRemoving(u); }}>
                <Trash2 className="size-4" />
              </Button>
            )}
          </li>
        ))}
      </ul>
      {setAdmin.error && <div className="mt-2"><ErrorText error={setAdmin.error} /></div>}
      <p className="mt-2 text-xs text-muted">Admins can make other members admin and mark split payments as received.</p>
      <Sheet open={!!removing} onClose={() => setRemoving(null)} title={`Remove ${removing?.name ?? ""}?`}>
        <p className="text-sm">
          They'll be signed out on every device and won't be able to sign in again. Everything they added stays.
        </p>
        <ErrorText error={remove.error} />
        <div className="mt-4 flex gap-2">
          <Button variant="danger" className="flex-1" busy={remove.isPending} onClick={() => removing && remove.mutate(removing.id)}>Remove</Button>
          <Button variant="ghost" onClick={() => setRemoving(null)}>Cancel</Button>
        </div>
      </Sheet>
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

function ChangePasswordSheet({ open, onClose }: { open: boolean; onClose: () => void }) {
  const blank = { currentPassword: "", newPassword: "", confirm: "" };
  const [f, setF] = useState(blank);
  const change = useMutation({
    mutationFn: () => api("/auth/password", { body: { currentPassword: f.currentPassword, newPassword: f.newPassword } }),
    onSuccess: () => setF(blank),
  });
  const err = change.error instanceof ApiError ? change.error : undefined;
  const mismatch = f.confirm !== "" && f.confirm !== f.newPassword;
  const close = () => { setF(blank); change.reset(); onClose(); };
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.value });

  return (
    <Sheet open={open} onClose={close} title="Change password">
      {change.isSuccess ? (
        <div className="flex flex-col items-center gap-3 py-4 text-center">
          <Check className="size-8 text-accent" />
          <p className="font-medium">Password changed</p>
          <p className="text-sm text-muted">Use it next time you sign in. Devices already signed in stay signed in.</p>
          <Button variant="primary" onClick={close}>Done</Button>
        </div>
      ) : (
        <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); if (!mismatch) change.mutate(); }}>
          <Field label="Current password" error={err?.field("currentPassword")}>
            <Input type="password" autoComplete="current-password" value={f.currentPassword} onChange={set("currentPassword")} required />
          </Field>
          <Field label="New password" error={err?.field("newPassword")} hint="At least 8 characters">
            <Input type="password" autoComplete="new-password" value={f.newPassword} onChange={set("newPassword")} required minLength={8} />
          </Field>
          <Field label="New password again" error={mismatch ? "The two new passwords don't match." : undefined}>
            <Input type="password" autoComplete="new-password" value={f.confirm} onChange={set("confirm")} required />
          </Field>
          {change.error && !err?.errors && <ErrorText error={change.error} />}
          <Button variant="primary" busy={change.isPending} disabled={mismatch}>Change password</Button>
        </form>
      )}
    </Sheet>
  );
}
