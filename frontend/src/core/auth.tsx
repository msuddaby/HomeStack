import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent, type ReactNode } from "react";
import { useNavigate, useParams } from "react-router";
import { api, ApiError } from "./api";
import { Button, ErrorText, Field, Input, Spinner } from "./ui";

export interface Me {
  user: { id: number; name: string; username: string; isAdmin: boolean };
  household: { id: number; name: string };
  /** Runs this HomeStack: manages households and invites. */
  isInstanceAdmin: boolean;
}
interface AuthStatus { needsSetup: boolean; authenticated: boolean }

export function useMe() {
  return useQuery({ queryKey: ["me"], queryFn: () => api<Me>("/auth/me"), retry: false, staleTime: Infinity });
}

export function useLogout() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => api("/auth/logout", { method: "POST" }),
    onSuccess: () => qc.clear(),
    onSettled: () => window.location.assign("/"),
  });
}

/** Shows first-run setup or login until there's a signed-in user. */
export function AuthGate({ children }: { children: ReactNode }) {
  const status = useQuery({ queryKey: ["auth-status"], queryFn: () => api<AuthStatus>("/auth/status") });
  const me = useMe();

  if (status.isPending || (status.data?.authenticated && me.isPending)) return <Spinner />;
  if (status.error) return <Centered><ErrorText error={status.error} /></Centered>;
  if (status.data.needsSetup) return <SetupForm />;
  if (!status.data.authenticated || me.error instanceof ApiError) return <LoginForm />;
  return <>{children}</>;
}

function Centered({ children }: { children: ReactNode }) {
  return <div className="mx-auto flex min-h-dvh max-w-sm flex-col justify-center gap-6 px-4 py-10">{children}</div>;
}

function Brand({ subtitle }: { subtitle: string }) {
  return (
    <div className="flex flex-col items-center gap-3 text-center">
      <img src="/icon.svg" alt="" className="size-16" />
      <div>
        <h1 className="text-2xl font-bold">HomeStack</h1>
        <p className="text-sm text-muted">{subtitle}</p>
      </div>
    </div>
  );
}

function useAuthSubmit<T>(path: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: T) => api<Me>(path, { body }),
    onSuccess: (me) => {
      qc.setQueryData(["me"], me);
      qc.setQueryData(["auth-status"], { needsSetup: false, authenticated: true });
    },
  });
}

function LoginForm() {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const login = useAuthSubmit<{ username: string; password: string }>("/auth/login");
  const submit = (e: FormEvent) => { e.preventDefault(); login.mutate({ username, password }); };
  return (
    <Centered>
      <Brand subtitle="Sign in to your household" />
      <form onSubmit={submit} className="flex flex-col gap-4">
        <Field label="Username"><Input autoComplete="username" autoCapitalize="none" value={username} onChange={(e) => setUsername(e.target.value)} required /></Field>
        <Field label="Password"><Input type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required /></Field>
        <ErrorText error={login.error} />
        <Button variant="primary" busy={login.isPending}>Sign in</Button>
      </form>
    </Centered>
  );
}

function SetupForm() {
  const [f, setF] = useState({ householdName: "", name: "", username: "", password: "" });
  const setup = useAuthSubmit<typeof f>("/auth/setup");
  const err = setup.error instanceof ApiError ? setup.error : undefined;
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.value });
  return (
    <Centered>
      <Brand subtitle="Welcome! Set up your household to get started." />
      <form onSubmit={(e) => { e.preventDefault(); setup.mutate(f); }} className="flex flex-col gap-4">
        <Field label="Household name" error={err?.field("householdName")}><Input placeholder="The Smith House" value={f.householdName} onChange={set("householdName")} required /></Field>
        <Field label="Your name" error={err?.field("name")}><Input autoComplete="name" value={f.name} onChange={set("name")} required /></Field>
        <Field label="Username" error={err?.field("username")}><Input autoCapitalize="none" autoComplete="username" value={f.username} onChange={set("username")} required /></Field>
        <Field label="Password" error={err?.field("password")} hint="At least 8 characters"><Input type="password" autoComplete="new-password" value={f.password} onChange={set("password")} required minLength={8} /></Field>
        {setup.error && !err?.errors && <ErrorText error={setup.error} />}
        <Button variant="primary" busy={setup.isPending}>Create household</Button>
      </form>
    </Centered>
  );
}

interface InviteCheck { valid: boolean; note: string | null; expiresAt: string | null }

/** /join/:token — someone with an invite link creates their household and its first account. Works signed out. */
export function JoinPage() {
  const token = useParams().token ?? "";
  const qc = useQueryClient();
  const navigate = useNavigate();
  const status = useQuery({ queryKey: ["auth-status"], queryFn: () => api<AuthStatus>("/auth/status") });
  const me = useMe();
  const invite = useQuery({ queryKey: ["invite", token], queryFn: () => api<InviteCheck>(`/auth/invites/${encodeURIComponent(token)}`), retry: false });
  const [f, setF] = useState({ householdName: "", name: "", username: "", password: "" });
  const join = useAuthSubmit<typeof f & { token: string }>("/auth/join");
  const signOut = useMutation({
    mutationFn: () => api("/auth/logout", { method: "POST" }),
    onSuccess: () => { qc.removeQueries({ queryKey: ["me"] }); return qc.invalidateQueries({ queryKey: ["auth-status"] }); },
  });
  const err = join.error instanceof ApiError ? join.error : undefined;
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.value });

  if (status.isPending || invite.isPending || (status.data?.authenticated && me.isPending)) return <Spinner />;
  if (invite.error) return <Centered><ErrorText error={invite.error} /></Centered>;
  if (!invite.data.valid) {
    return (
      <Centered>
        <Brand subtitle="This invite has been used or has expired." />
        <p className="text-center text-sm text-muted">Ask whoever sent it for a new link.</p>
        <Button onClick={() => navigate("/")}>Go to sign in</Button>
      </Centered>
    );
  }
  if (status.data?.authenticated && me.data) {
    return (
      <Centered>
        <Brand subtitle="You've been invited to start a household." />
        <p className="text-center text-sm">
          You're signed in as <b>{me.data.user.username}</b> ({me.data.household.name}). Sign out to use this invite for a new household.
        </p>
        <div className="flex flex-col gap-2">
          <Button variant="primary" busy={signOut.isPending} onClick={() => signOut.mutate()}>Sign out and continue</Button>
          <Button variant="ghost" onClick={() => navigate("/")}>Back to {me.data.household.name}</Button>
        </div>
      </Centered>
    );
  }
  return (
    <Centered>
      <Brand subtitle={invite.data.note ? `Welcome, ${invite.data.note}! Set up your household.` : "You've been invited. Set up your household."} />
      <form className="flex flex-col gap-4" onSubmit={(e) => {
        e.preventDefault();
        join.mutate({ ...f, token }, { onSuccess: () => navigate("/", { replace: true }) });
      }}>
        <Field label="Household name" error={err?.field("householdName")}><Input placeholder="The Smith House" value={f.householdName} onChange={set("householdName")} required /></Field>
        <Field label="Your name" error={err?.field("name")}><Input autoComplete="name" value={f.name} onChange={set("name")} required /></Field>
        <Field label="Username" error={err?.field("username")}><Input autoCapitalize="none" autoComplete="username" value={f.username} onChange={set("username")} required /></Field>
        <Field label="Password" error={err?.field("password")} hint="At least 8 characters"><Input type="password" autoComplete="new-password" value={f.password} onChange={set("password")} required minLength={8} /></Field>
        {join.error && !err?.errors && <ErrorText error={join.error} />}
        <Button variant="primary" busy={join.isPending}>Create household</Button>
        <p className="text-center text-xs text-muted">You'll be its admin, and can add the rest of your household in Settings.</p>
      </form>
    </Centered>
  );
}
