import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link } from "react-router";
import { ArrowLeft, Check, ChevronRight, Copy, KeyRound, Link2, Share2, Sparkles, Trash2 } from "lucide-react";
import { api, ApiError, errorMessage } from "@/core/api";
import { useMe } from "@/core/auth";
import { PageHeader } from "@/core/layout";
import { Button, Card, Chip, cx, Empty, ErrorText, Field, Input, Select, Sheet, Spinner } from "@/core/ui";

interface AdminHousehold {
  id: number; name: string; createdAt: string; members: number; admins: string[]; isMine: boolean;
  /** Has its own AI (used even when sharing is on). */
  ownAi: boolean;
  /** You share your AI with it. */
  sharedAi: boolean;
}
interface AdminUser { id: number; name: string; username: string; isAdmin: boolean }
interface Invite { id: number; note: string | null; createdAt: string; expiresAt: string; usedAt: string | null; household: string | null; revokedAt: string | null }
interface InstanceSettings { mainHousehold: string | null; mainHasAi: boolean }

const HOUSEHOLDS = ["admin", "households"];
const INVITES = ["admin", "invites"];
const SETTINGS = ["admin", "settings"];

const day = (iso: string) => new Date(iso).toLocaleDateString([], { month: "short", day: "numeric" });

/** The instance admin's panel: every household on this HomeStack, invite links, and sharing the AI. Never their data. */
export function AdminPage() {
  const me = useMe();
  if (!me.data) return <Spinner />;
  if (!me.data.isInstanceAdmin) return <Empty title="Only the HomeStack admin can see this." />;
  return (
    <>
      <Link to="/settings" className="mb-2 inline-flex items-center gap-1 text-sm text-muted"><ArrowLeft className="size-4" />Settings</Link>
      <PageHeader title="Manage HomeStack" subtitle="Households on this server" />
      <div className="flex flex-col gap-4">
        <Invites />
        <Households />
        <SharedAi />
      </div>
    </>
  );
}

function Households() {
  const households = useQuery({ queryKey: HOUSEHOLDS, queryFn: () => api<AdminHousehold[]>("/admin/households") });
  const [open, setOpen] = useState<AdminHousehold | null>(null);
  return (
    <Card title="Households">
      {households.error && <ErrorText error={households.error} />}
      {!households.data ? <Spinner /> : (
        <ul className="divide-y divide-border">
          {households.data.map((h) => (
            <li key={h.id}>
              <button type="button" onClick={() => setOpen(h)} className="flex w-full items-center gap-3 py-2.5 text-left">
                <div className="min-w-0 flex-1">
                  <p className="flex items-center gap-2 truncate text-sm font-medium">{h.name}{h.isMine && <Chip tone="accent">Yours</Chip>}</p>
                  <p className="truncate text-xs text-muted">
                    {h.members} {h.members === 1 ? "member" : "members"} · admin {h.admins.join(", ") || "none"} ·{" "}
                    {h.ownAi ? "own AI" : h.sharedAi ? "your AI" : h.isMine ? "no AI yet" : "no AI"}
                  </p>
                </div>
                <ChevronRight className="size-4 shrink-0 text-muted" />
              </button>
            </li>
          ))}
        </ul>
      )}
      <HouseholdSheet household={open} onClose={() => setOpen(null)} />
    </Card>
  );
}

/** Rename, members (and resetting a forgotten password), and deleting. */
function HouseholdSheet({ household: h, onClose }: { household: AdminHousehold | null; onClose: () => void }) {
  const qc = useQueryClient();
  const users = useQuery({ queryKey: ["admin", "users", h?.id], queryFn: () => api<AdminUser[]>(`/admin/households/${h!.id}/users`), enabled: !!h });
  const [name, setName] = useState("");
  const [confirm, setConfirm] = useState("");
  const [resetting, setResetting] = useState<AdminUser | null>(null);
  const [password, setPassword] = useState("");
  const [shownFor, setShownFor] = useState<number | null>(null);
  if (h && shownFor !== h.id) { setShownFor(h.id); setName(h.name); setConfirm(""); setResetting(null); }

  const rename = useMutation({
    mutationFn: () => api(`/admin/households/${h!.id}`, { method: "PUT", body: { name } }),
    onSuccess: () => qc.invalidateQueries({ queryKey: HOUSEHOLDS }),
  });
  const reset = useMutation({
    mutationFn: () => api(`/admin/users/${resetting!.id}/password`, { body: { newPassword: password } }),
    onSuccess: () => { setResetting(null); setPassword(""); },
  });
  const del = useMutation({
    mutationFn: () => api(`/admin/households/${h!.id}?confirmName=${encodeURIComponent(confirm)}`, { method: "DELETE" }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: HOUSEHOLDS }); onClose(); },
  });
  const renameErr = rename.error instanceof ApiError ? rename.error : undefined;
  const resetErr = reset.error instanceof ApiError ? reset.error : undefined;

  return (
    <Sheet open={!!h} onClose={onClose} title={h?.name ?? ""}>
      {h && (
        <div className="flex flex-col gap-5">
          <form className="flex items-end gap-2" onSubmit={(e) => { e.preventDefault(); rename.mutate(); }}>
            <Field label="Name" className="flex-1" error={renameErr?.field("name")}><Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={80} /></Field>
            <Button disabled={name.trim() === h.name || !name.trim()} busy={rename.isPending}>{rename.isSuccess && name.trim() === h.name ? <Check className="size-4" /> : "Rename"}</Button>
          </form>

          <div>
            <p className="mb-1 text-sm font-medium">Members</p>
            {!users.data ? <Spinner /> : (
              <ul className="divide-y divide-border">
                {users.data.map((u) => (
                  <li key={u.id} className="py-2">
                    <div className="flex items-center gap-2">
                      <div className="min-w-0 flex-1">
                        <p className="flex items-center gap-2 text-sm">{u.name}{u.isAdmin && <Chip tone="accent">Admin</Chip>}</p>
                        <p className="text-xs text-muted">@{u.username}</p>
                      </div>
                      <Button variant="ghost" className="min-h-9 px-2 text-xs" onClick={() => { reset.reset(); setPassword(""); setResetting(resetting?.id === u.id ? null : u); }}>
                        <KeyRound className="size-4" />Reset password
                      </Button>
                    </div>
                    {resetting?.id === u.id && (
                      <form className="mt-2 flex items-end gap-2" onSubmit={(e) => { e.preventDefault(); reset.mutate(); }}>
                        <Field label={`New password for ${u.name}`} className="flex-1" error={resetErr?.field("newPassword")} hint="Tell them, and ask them to change it in Settings.">
                          <Input type="text" autoComplete="off" value={password} onChange={(e) => setPassword(e.target.value)} required minLength={8} />
                        </Field>
                        <Button variant="primary" busy={reset.isPending}>Set</Button>
                      </form>
                    )}
                  </li>
                ))}
              </ul>
            )}
            {reset.isSuccess && <p className="mt-1 text-sm text-accent">Password changed.</p>}
            {reset.error && !resetErr?.errors && <ErrorText error={reset.error} />}
          </div>

          {h.isMine ? (
            <p className="text-xs text-muted">This is your own household, so it can't be deleted here.</p>
          ) : (
            <div className="rounded-xl border border-danger/40 p-3">
              <p className="text-sm font-medium text-danger">Delete household</p>
              <p className="mt-1 text-xs text-muted">
                Everything in it goes for good: members' accounts, inventory, recipes, receipts, bills and photos. Type <b className="text-text">{h.name}</b> to confirm.
              </p>
              <div className="mt-2 flex gap-2">
                <Input value={confirm} onChange={(e) => setConfirm(e.target.value)} placeholder={h.name} aria-label="Household name to confirm" autoComplete="off" />
                <Button variant="danger" disabled={confirm.trim().toLowerCase() !== h.name.toLowerCase()} busy={del.isPending} onClick={() => del.mutate()}>
                  <Trash2 className="size-4" />Delete
                </Button>
              </div>
              {del.error && <div className="mt-2"><ErrorText error={del.error} /></div>}
            </div>
          )}
        </div>
      )}
    </Sheet>
  );
}

/** Invite links: each one lets someone create a new household and its first account. */
function Invites() {
  const qc = useQueryClient();
  const invites = useQuery({ queryKey: INVITES, queryFn: () => api<Invite[]>("/admin/invites") });
  const [creating, setCreating] = useState(false);
  const [f, setF] = useState({ note: "", days: 7 });
  const [link, setLink] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const create = useMutation({
    mutationFn: () => api<{ invite: Invite; token: string }>("/admin/invites", { body: { note: f.note || null, days: f.days } }),
    onSuccess: (r) => { setLink(`${location.origin}/join/${r.token}`); qc.invalidateQueries({ queryKey: INVITES }); },
  });
  const revoke = useMutation({
    mutationFn: (id: number) => api(`/admin/invites/${id}`, { method: "DELETE" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: INVITES }),
  });
  const close = () => { setCreating(false); setLink(null); setCopied(false); create.reset(); setF({ note: "", days: 7 }); };

  async function copy() {
    try { await navigator.clipboard.writeText(link!); setCopied(true); setTimeout(() => setCopied(false), 1500); } catch { /* clipboard blocked */ }
  }
  async function share() {
    try { await navigator.share({ title: "Join HomeStack", text: "Set up your household on HomeStack:", url: link! }); } catch { /* cancelled */ }
  }

  const now = Date.now();
  const list = invites.data ?? [];
  const status = (i: Invite) =>
    i.usedAt ? { label: i.household ? `Used: ${i.household}` : "Used (household deleted)", tone: "accent" as const }
    : i.revokedAt ? { label: "Revoked", tone: "neutral" as const }
    : new Date(i.expiresAt).getTime() < now ? { label: "Expired", tone: "neutral" as const }
    : { label: `Open until ${day(i.expiresAt)}`, tone: "warn" as const };

  return (
    <Card title={<span className="flex items-center gap-2"><Link2 className="size-4" />Invites</span>}
      action={<Button variant="ghost" onClick={() => setCreating(true)}>New invite</Button>}>
      <p className="text-sm text-muted">Send someone a link: they set up their own household and account. Each link works once.</p>
      {invites.error && <ErrorText error={invites.error} />}
      {list.length > 0 && (
        <ul className="mt-2 divide-y divide-border">
          {list.map((i) => {
            const st = status(i);
            const open = !i.usedAt && !i.revokedAt && new Date(i.expiresAt).getTime() >= now;
            return (
              <li key={i.id} className="flex items-center gap-2 py-2">
                <div className="min-w-0 flex-1">
                  <p className={cx("truncate text-sm", !open && !i.usedAt && "text-muted")}>{i.note ?? "Invite"}</p>
                  <p className="text-xs text-muted">Made {day(i.createdAt)}</p>
                </div>
                <Chip tone={st.tone}>{st.label}</Chip>
                {open && (
                  <Button variant="ghost" className="min-h-9 px-2 text-xs" busy={revoke.isPending && revoke.variables === i.id} onClick={() => revoke.mutate(i.id)}>Revoke</Button>
                )}
              </li>
            );
          })}
        </ul>
      )}

      <Sheet open={creating} onClose={close} title={link ? "Invite ready" : "New invite"}>
        {link ? (
          <div className="flex flex-col gap-3">
            <p className="text-sm">Send this link. It's shown only now, works once, and expires in {f.days} {f.days === 1 ? "day" : "days"}.</p>
            <p className="break-all rounded-xl bg-surface-2 p-3 font-mono text-xs">{link}</p>
            <div className="flex gap-2">
              {"share" in navigator && <Button variant="primary" className="flex-1" onClick={share}><Share2 className="size-4" />Share</Button>}
              <Button className="flex-1" onClick={copy}>{copied ? <><Check className="size-4" />Copied</> : <><Copy className="size-4" />Copy link</>}</Button>
            </div>
            <Button variant="ghost" onClick={close}>Done</Button>
          </div>
        ) : (
          <form className="flex flex-col gap-3" onSubmit={(e) => { e.preventDefault(); create.mutate(); }}>
            <Field label="Who it's for (optional)" hint="Shown on their sign-up page and in your list.">
              <Input value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder="The Smiths" maxLength={80} />
            </Field>
            <Field label="Expires after">
              <Select value={f.days} onChange={(e) => setF({ ...f, days: Number(e.target.value) })}>
                {[1, 3, 7, 14, 30].map((d) => <option key={d} value={d}>{d} {d === 1 ? "day" : "days"}</option>)}
              </Select>
            </Field>
            {create.error && <ErrorText error={create.error} />}
            <Button variant="primary" busy={create.isPending}>Make link</Button>
          </form>
        )}
      </Sheet>
    </Card>
  );
}

/** Pick which households can use the admin's AI (while they have none of their own). */
function SharedAi() {
  const qc = useQueryClient();
  const settings = useQuery({ queryKey: SETTINGS, queryFn: () => api<InstanceSettings>("/admin/settings") });
  const households = useQuery({ queryKey: HOUSEHOLDS, queryFn: () => api<AdminHousehold[]>("/admin/households") });
  const share = useMutation({
    mutationFn: ({ id, shared }: { id: number; shared: boolean }) => api(`/admin/households/${id}/shared-ai`, { method: "PUT", body: { shared } }),
    // Tick straight away; the list is refetched to confirm.
    onMutate: ({ id, shared }) => qc.setQueryData<AdminHousehold[]>(HOUSEHOLDS, (list) => list?.map((h) => (h.id === id ? { ...h, sharedAi: shared } : h))),
    onSettled: () => qc.invalidateQueries({ queryKey: HOUSEHOLDS }),
  });
  const s = settings.data;
  const others = (households.data ?? []).filter((h) => !h.isMine);
  return (
    <Card title={<span className="flex items-center gap-2"><Sparkles className="size-4" />Shared AI</span>}>
      {!s || !households.data ? <Spinner /> : (
        <>
          <p className="text-sm">Let these households use {s.mainHousehold ? <b>{s.mainHousehold}</b> : "your household"}'s AI:</p>
          {!s.mainHasAi && (
            <p className="mt-2 rounded-xl bg-surface-2 p-3 text-sm">Set up AI for your own household in Settings first; until then there's nothing to share.</p>
          )}
          {others.length === 0 ? (
            <p className="mt-2 text-sm text-muted">No other households yet. Make an invite above.</p>
          ) : (
            <ul className="mt-1 divide-y divide-border">
              {others.map((h) => (
                <li key={h.id}>
                  <label className="flex items-center gap-3 py-2.5 text-sm">
                    <input type="checkbox" checked={h.sharedAi} onChange={(e) => share.mutate({ id: h.id, shared: e.target.checked })}
                      className="size-5 shrink-0 accent-[var(--color-accent)]" />
                    <span className="min-w-0 flex-1 truncate">{h.name}</span>
                    {h.ownAi && <Chip>Has its own</Chip>}
                  </label>
                </li>
              ))}
            </ul>
          )}
          <p className="mt-2 text-xs text-muted">
            They can read receipts, bills and lists with it, but never see its key or settings. What they use is billed to your AI account.
            A household with its own AI ("Has its own") uses that instead, even if ticked.
          </p>
          {share.error && <p className="mt-2 text-sm text-danger">{errorMessage(share.error)}</p>}
        </>
      )}
    </Card>
  );
}
