import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Bell, Check, Copy, DatabaseBackup, Send } from "lucide-react";
import { api, ApiError, errorMessage } from "@/core/api";
import { Button, Card, cx, ErrorText, Field, Input, Select } from "@/core/ui";

interface NotificationSettings {
  enabled: boolean; serverUrl: string; topic: string; hasAccessToken: boolean; digestHour: number;
  lastSentAt: string | null; lastError: string | null; saved: boolean;
}
interface DigestMessage { title: string; body: string; urgent: boolean; isEmpty: boolean }
interface BackupFile { name: string; sizeBytes: number; createdAt: string }
interface Backups { directory: string; keep: number; files: BackupFile[] }

const hourLabel = (h: number) => new Date(2000, 0, 1, h).toLocaleTimeString([], { hour: "numeric" });
const when = (iso: string) => new Date(iso).toLocaleString([], { weekday: "short", month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });

export function NotificationsCard() {
  const qc = useQueryClient();
  const settings = useQuery({ queryKey: ["notifications"], queryFn: () => api<NotificationSettings>("/notifications/settings") });
  const [f, setF] = useState<{ enabled: boolean; serverUrl: string; topic: string; accessToken: string; digestHour: number } | null>(null);
  const [showServer, setShowServer] = useState(false);
  const [copied, setCopied] = useState(false);
  const [preview, setPreview] = useState<DigestMessage | null>(null);

  useEffect(() => {
    const s = settings.data;
    if (s && !f) {
      setF({ enabled: s.saved ? s.enabled : true, serverUrl: s.serverUrl, topic: s.topic, accessToken: "", digestHour: s.digestHour });
      setShowServer(s.serverUrl !== "https://ntfy.sh" || s.hasAccessToken);
    }
  }, [settings.data, f]);

  // accessToken: null keeps the saved one, "" removes it.
  const save = useMutation({
    mutationFn: (opts: { clearToken?: boolean } = {}) => api<NotificationSettings>("/notifications/settings", {
      method: "PUT", body: { ...f, accessToken: opts.clearToken ? "" : f!.accessToken === "" ? null : f!.accessToken },
    }),
    onSuccess: (s) => { qc.setQueryData(["notifications"], s); setF((p) => p && { ...p, accessToken: "" }); },
  });
  const test = useMutation({
    mutationFn: async () => { if (dirty) await save.mutateAsync({}); return api<DigestMessage>("/notifications/test", { method: "POST" }); },
    onSettled: () => qc.invalidateQueries({ queryKey: ["notifications"] }),
  });
  const showPreview = useMutation({ mutationFn: () => api<DigestMessage>("/notifications/preview"), onSuccess: setPreview });

  const s = settings.data;
  if (!s || !f) return <Card title="Daily reminder"><p className="text-sm text-muted">Loading…</p></Card>;
  const dirty = !s.saved || f.enabled !== s.enabled || f.serverUrl !== s.serverUrl || f.topic !== s.topic || f.digestHour !== s.digestHour || f.accessToken !== "";
  const err = save.error instanceof ApiError ? save.error : undefined;
  const set = <K extends keyof typeof f>(k: K, v: (typeof f)[K]) => setF({ ...f, [k]: v });

  async function copyTopic() {
    try { await navigator.clipboard.writeText(f!.topic); setCopied(true); setTimeout(() => setCopied(false), 1500); } catch { /* clipboard blocked */ }
  }

  return (
    <Card title={<span className="flex items-center gap-2"><Bell className="size-4" />Daily reminder</span>}>
      <p className="mb-3 text-sm text-muted">
        A morning push to your phone with what to freeze, what's expiring and what's on the menu. Uses the free <b className="text-text">ntfy</b> app.
      </p>
      <form className="flex flex-col gap-3" onSubmit={(e) => { e.preventDefault(); save.mutate({}); }}>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={f.enabled} onChange={(e) => set("enabled", e.target.checked)} className="size-4 accent-[var(--color-accent)]" />
          Send every day
        </label>
        <div className="grid grid-cols-[1fr_auto] gap-3">
          <Field label="Topic" error={err?.field("topic")} hint="Keep it hard to guess: anyone who knows a topic name can read it.">
            <div className="flex gap-2">
              <Input value={f.topic} onChange={(e) => set("topic", e.target.value)} autoCapitalize="none" autoComplete="off" spellCheck={false} />
              <Button type="button" className="shrink-0 px-3" onClick={copyTopic} aria-label="Copy topic">{copied ? <Check className="size-4" /> : <Copy className="size-4" />}</Button>
            </div>
          </Field>
          <Field label="At" error={err?.field("digestHour")}>
            <Select value={f.digestHour} onChange={(e) => set("digestHour", Number(e.target.value))}>
              {Array.from({ length: 18 }, (_, i) => i + 5).map((h) => <option key={h} value={h}>{hourLabel(h)}</option>)}
            </Select>
          </Field>
        </div>

        {showServer ? (
          <div className="grid gap-3">
            <Field label="ntfy server" error={err?.field("serverUrl")}><Input value={f.serverUrl} onChange={(e) => set("serverUrl", e.target.value)} inputMode="url" autoCapitalize="none" /></Field>
            <Field label="Access token" hint={s.hasAccessToken ? "Saved. Type a new one to replace it, or clear it below." : "Only needed for a protected topic on your own server."}>
              <Input type="password" value={f.accessToken} onChange={(e) => set("accessToken", e.target.value)} autoComplete="off" placeholder={s.hasAccessToken ? "••••••••" : "tk_…"} />
            </Field>
            {s.hasAccessToken && <button type="button" className="self-start text-xs text-danger" onClick={() => save.mutate({ clearToken: true })}>Remove saved token</button>}
          </div>
        ) : (
          <button type="button" className="self-start text-xs text-muted underline" onClick={() => setShowServer(true)}>Using your own ntfy server?</button>
        )}

        <details className="rounded-xl bg-surface-2 px-3 py-2 text-sm">
          <summary className="cursor-pointer font-medium">Set up on your phone</summary>
          <ol className="mt-2 list-decimal space-y-1 pl-5 text-muted">
            <li>Install <b className="text-text">ntfy</b> from the App Store or Google Play.</li>
            <li>Tap <b className="text-text">+</b>, enter the topic above{f.serverUrl !== "https://ntfy.sh" && <> and your server <b className="text-text">{f.serverUrl}</b></>}, then subscribe.</li>
            <li>Save here and tap <b className="text-text">Send test</b>.</li>
          </ol>
        </details>

        {save.error && !err?.errors && <ErrorText error={save.error} />}
        {test.error && <p className="text-sm text-danger">Couldn't send: {errorMessage(test.error)}</p>}
        {test.isSuccess && !test.isPending && <p className="text-sm text-accent">Sent. Check your phone.</p>}
        {!test.error && s.lastError && <p className="text-sm text-danger">Last send failed: {s.lastError}</p>}
        {s.lastSentAt && !s.lastError && <p className="text-xs text-muted">Last sent {when(s.lastSentAt)}</p>}

        <div className="flex flex-wrap gap-2">
          <Button variant="primary" disabled={!dirty} busy={save.isPending}>{s.saved && !dirty ? <><Check className="size-4" />Saved</> : "Save"}</Button>
          <Button type="button" busy={test.isPending} onClick={() => test.mutate()}><Send className="size-4" />Send test</Button>
          <Button type="button" variant="ghost" busy={showPreview.isPending} onClick={() => preview ? setPreview(null) : showPreview.mutate()}>{preview ? "Hide preview" : "Preview"}</Button>
        </div>
        {preview && (
          <div className="rounded-xl border border-border p-3 text-sm">
            <p className="font-semibold">{preview.title}</p>
            <p className="mt-1 whitespace-pre-line text-muted">{preview.body}</p>
            {preview.isEmpty && <p className="mt-2 text-xs text-muted">On days like this nothing is sent.</p>}
          </div>
        )}
      </form>
    </Card>
  );
}

const size = (b: number) => b < 1024 * 1024 ? `${Math.max(1, Math.round(b / 1024))} KB` : `${(b / 1024 / 1024).toFixed(1)} MB`;

export function BackupsCard() {
  const qc = useQueryClient();
  const backups = useQuery({ queryKey: ["backups"], queryFn: () => api<Backups>("/system/backups") });
  const run = useMutation({
    mutationFn: () => api<BackupFile>("/system/backups", { method: "POST" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["backups"] }),
  });
  const b = backups.data;
  const latest = b?.files[0];

  return (
    <Card title={<span className="flex items-center gap-2"><DatabaseBackup className="size-4" />Backups</span>}
      action={<Button variant="ghost" busy={run.isPending} onClick={() => run.mutate()}>Back up now</Button>}>
      {b && (
        <>
          <p className={cx("text-sm", latest ? "text-text" : "text-warn")}>
            {latest ? <>Last backup {when(latest.createdAt)} · {size(latest.sizeBytes)}</> : "No backups yet. The first one runs tonight."}
          </p>
          <p className="mt-1 text-xs text-muted">
            Taken nightly, keeping the last {b.keep}. Saved to <code className="break-all">{b.directory}</code>
            {b.files.length > 1 && <> · {b.files.length} on hand</>}
          </p>
        </>
      )}
      {(backups.error || run.error) && <ErrorText error={backups.error ?? run.error} />}
    </Card>
  );
}
