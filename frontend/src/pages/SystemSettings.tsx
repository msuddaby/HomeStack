import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Bell, BellOff, Check, Copy, DatabaseBackup, Send, Share, Sparkles } from "lucide-react";
import { AI_KEY, AI_PRESETS, useAiSettings, type AiProviderKind, type AiSettings, type AiTestResult } from "@/core/ai";
import { api, ApiError, errorMessage } from "@/core/api";
import { usePush } from "@/core/push";
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
  const [f, setF] = useState<{ enabled: boolean; ntfy: boolean; serverUrl: string; topic: string; accessToken: string; digestHour: number } | null>(null);
  const [showServer, setShowServer] = useState(false);
  const [copied, setCopied] = useState(false);
  const [preview, setPreview] = useState<DigestMessage | null>(null);

  useEffect(() => {
    const s = settings.data;
    if (s && !f) {
      setF({
        enabled: s.saved ? s.enabled : true, ntfy: s.saved && s.topic !== "", serverUrl: s.serverUrl,
        topic: s.topic || newTopic(), accessToken: "", digestHour: s.digestHour,
      });
      setShowServer(s.serverUrl !== "https://ntfy.sh" || s.hasAccessToken);
    }
  }, [settings.data, f]);

  // accessToken: null keeps the saved one, "" removes it. No ntfy: an empty topic.
  const save = useMutation({
    mutationFn: (opts: { clearToken?: boolean } = {}) => api<NotificationSettings>("/notifications/settings", {
      method: "PUT",
      body: {
        enabled: f!.enabled, serverUrl: f!.serverUrl, topic: f!.ntfy ? f!.topic : "", digestHour: f!.digestHour,
        accessToken: opts.clearToken ? "" : f!.accessToken === "" ? null : f!.accessToken,
      },
    }),
    onSuccess: (s) => { qc.setQueryData(["notifications"], s); setF((p) => p && { ...p, accessToken: "" }); },
  });
  const test = useMutation({
    mutationFn: async () => { if (dirty) await save.mutateAsync({}); return api<DigestMessage>("/notifications/test", { method: "POST" }); },
    onSettled: () => { qc.invalidateQueries({ queryKey: ["notifications"] }); qc.invalidateQueries({ queryKey: ["push"] }); },
  });
  const showPreview = useMutation({ mutationFn: () => api<DigestMessage>("/notifications/preview"), onSuccess: setPreview });

  const s = settings.data;
  if (!s || !f) return <Card title="Notifications"><p className="text-sm text-muted">Loading…</p></Card>;
  const topic = f.ntfy ? f.topic : "";
  const dirty = !s.saved || f.enabled !== s.enabled || topic !== s.topic || f.digestHour !== s.digestHour
    || (f.ntfy && (f.serverUrl !== s.serverUrl || f.accessToken !== ""));
  const err = save.error instanceof ApiError ? save.error : undefined;
  const set = <K extends keyof typeof f>(k: K, v: (typeof f)[K]) => setF({ ...f, [k]: v });

  async function copyTopic() {
    try { await navigator.clipboard.writeText(f!.topic); setCopied(true); setTimeout(() => setCopied(false), 1500); } catch { /* clipboard blocked */ }
  }

  return (
    <Card title={<span className="flex items-center gap-2"><Bell className="size-4" />Notifications</span>}>
      <p className="mb-3 text-sm text-muted">
        A morning reminder with what to freeze, what's expiring, bills due and what's on the menu.
      </p>
      <ThisDevice onChange={() => qc.invalidateQueries({ queryKey: ["notifications"] })} />

      <form className="mt-4 flex flex-col gap-3 border-t border-border pt-4" onSubmit={(e) => { e.preventDefault(); save.mutate({}); }}>
        <div className="flex items-center gap-3">
          <label className="flex flex-1 items-center gap-2 text-sm">
            <input type="checkbox" checked={f.enabled} onChange={(e) => set("enabled", e.target.checked)} className="size-4 accent-[var(--color-accent)]" />
            Daily reminder
          </label>
          <Field label="At" error={err?.field("digestHour")}>
            <Select value={f.digestHour} disabled={!f.enabled} onChange={(e) => set("digestHour", Number(e.target.value))}>
              {Array.from({ length: 18 }, (_, i) => i + 5).map((h) => <option key={h} value={h}>{hourLabel(h)}</option>)}
            </Select>
          </Field>
        </div>
        <p className="-mt-2 text-xs text-muted">Goes to everyone in the household who has notifications on. Nothing is sent on days with nothing to say.</p>

        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={f.ntfy} onChange={(e) => set("ntfy", e.target.checked)} className="size-4 accent-[var(--color-accent)]" />
          Also send to the <b>ntfy</b> app
        </label>
        {f.ntfy && (
          <div className="flex flex-col gap-3 rounded-xl border border-border p-3">
            <Field label="Topic" error={err?.field("topic")} hint="Keep it hard to guess: anyone who knows a topic name can read it.">
              <div className="flex gap-2">
                <Input value={f.topic} onChange={(e) => set("topic", e.target.value)} autoCapitalize="none" autoComplete="off" spellCheck={false} />
                <Button type="button" className="shrink-0 px-3" onClick={copyTopic} aria-label="Copy topic">{copied ? <Check className="size-4" /> : <Copy className="size-4" />}</Button>
              </div>
            </Field>
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
              <summary className="cursor-pointer font-medium">Set up ntfy on your phone</summary>
              <ol className="mt-2 list-decimal space-y-1 pl-5 text-muted">
                <li>Install <b className="text-text">ntfy</b> from the App Store or Google Play.</li>
                <li>Tap <b className="text-text">+</b>, enter the topic above{f.serverUrl !== "https://ntfy.sh" && <> and your server <b className="text-text">{f.serverUrl}</b></>}, then subscribe.</li>
                <li>Save here and tap <b className="text-text">Send test</b>.</li>
              </ol>
            </details>
            {!test.error && s.lastError && <p className="text-sm text-danger">Last ntfy send failed: {s.lastError}</p>}
          </div>
        )}

        {save.error && !err?.errors && <ErrorText error={save.error} />}
        {test.error && <p className="text-sm text-danger">Couldn't send: {errorMessage(test.error)}</p>}
        {test.isSuccess && !test.isPending && <p className="text-sm text-accent">Sent. Check your phone.</p>}
        {s.lastSentAt && !s.lastError && f.ntfy && <p className="text-xs text-muted">Last sent to ntfy {when(s.lastSentAt)}</p>}

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

/** A hard-to-guess ntfy topic, since anyone who knows a public topic can read it. */
function newTopic() {
  const alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
  return "homestack-" + Array.from(crypto.getRandomValues(new Uint8Array(12)), (b) => alphabet[b % alphabet.length]).join("");
}

/** Turning notifications on for the phone or computer you're using, and your other devices that have them. */
function ThisDevice({ onChange }: { onChange: () => void }) {
  const push = usePush();
  const error = push.turnOn.error ?? push.turnOff.error ?? push.remove.error;
  return (
    <div className="flex flex-col gap-2">
      {push.state === "on" ? (
        <div className="flex items-center gap-2">
          <p className="flex flex-1 items-center gap-1.5 text-sm text-accent"><Check className="size-4" />On for this {push.thisDevice?.device ?? "device"}</p>
          <Button variant="ghost" busy={push.turnOff.isPending} onClick={() => push.turnOff.mutate()}><BellOff className="size-4" />Turn off</Button>
        </div>
      ) : push.state === "off" ? (
        <Button variant="primary" className="self-start" busy={push.turnOn.isPending} onClick={() => push.turnOn.mutate(undefined, { onSuccess: onChange })}>
          <Bell className="size-4" />Turn on notifications
        </Button>
      ) : push.state === "install" ? (
        <p className="rounded-xl bg-surface-2 p-3 text-sm">
          On iPhone and iPad, notifications come from the Home Screen app: tap <Share className="inline size-4 align-text-bottom" /> <b>Share</b> →{" "}
          <b>Add to Home Screen</b>, open HomeStack from there, and turn them on here.
        </p>
      ) : push.state === "blocked" ? (
        <p className="rounded-xl bg-surface-2 p-3 text-sm">
          Notifications are blocked for HomeStack. Allow them in <b>Settings → Notifications → HomeStack</b> (iPhone) or in this site's settings in your browser, then reload.
        </p>
      ) : push.state === "unsupported" ? (
        <p className="text-sm text-muted">This browser can't show notifications from HomeStack.</p>
      ) : null}
      {push.thisDevice?.lastError && <p className="text-sm text-danger">Last notification to this device failed: {push.thisDevice.lastError}</p>}
      {error && <p className="text-sm text-danger">{errorMessage(error)}</p>}
      {push.otherDevices.length > 0 && (
        <ul className="flex flex-col gap-1 text-xs text-muted">
          {push.otherDevices.map((d) => (
            <li key={d.id} className="flex items-center gap-2">
              <span className="flex-1">Also on your {d.device ?? "other device"}{d.lastError && <span className="text-danger"> (last one failed)</span>}</span>
              <button type="button" className="text-danger" disabled={push.remove.isPending} onClick={() => push.remove.mutate(d.id)}>Remove</button>
            </li>
          ))}
        </ul>
      )}
    </div>
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

export function AiCard() {
  const qc = useQueryClient();
  const settings = useAiSettings();
  const [f, setF] = useState<{ provider: AiProviderKind; baseUrl: string; apiKey: string; model: string; visionModel: string } | null>(null);
  const [presetId, setPresetId] = useState<string>("deepseek");
  const [advanced, setAdvanced] = useState(false);

  useEffect(() => {
    const s = settings.data;
    if (s && !f) {
      setF({ provider: s.provider, baseUrl: s.baseUrl, apiKey: "", model: s.model, visionModel: s.visionModel ?? "" });
      const match = AI_PRESETS.find((p) => p.provider === s.provider && p.baseUrl === s.baseUrl);
      setPresetId(match?.id ?? "custom");
      setAdvanced(!!s.visionModel || !match);
    }
  }, [settings.data, f]);

  const body = (clearKey = false) => ({ ...f, apiKey: clearKey ? "" : f!.apiKey === "" ? null : f!.apiKey, visionModel: f!.visionModel || null });
  const save = useMutation({
    mutationFn: (opts: { clearKey?: boolean } = {}) => api<AiSettings>("/ai/settings", { method: "PUT", body: body(opts.clearKey) }),
    onSuccess: (s) => { qc.setQueryData(AI_KEY, s); setF((p) => p && { ...p, apiKey: "" }); },
  });
  const test = useMutation({ mutationFn: () => api<AiTestResult>("/ai/test", { body: body() }) });
  const remove = useMutation({
    mutationFn: () => api("/ai/settings", { method: "DELETE" }),
    onSuccess: () => { setF(null); test.reset(); qc.invalidateQueries({ queryKey: AI_KEY }); },
  });

  const s = settings.data;
  if (!s || !f) return <Card title="AI"><p className="text-sm text-muted">Loading…</p></Card>;
  const preset = AI_PRESETS.find((p) => p.id === presetId)!;
  const err = save.error instanceof ApiError ? save.error : test.error instanceof ApiError ? test.error : undefined;
  const set = <K extends keyof typeof f>(k: K, v: (typeof f)[K]) => { setF({ ...f, [k]: v }); test.reset(); };

  function choose(id: string) {
    const p = AI_PRESETS.find((x) => x.id === id)!;
    setPresetId(id);
    test.reset();
    if (id === "custom") return setAdvanced(true);
    setF({ ...f!, provider: p.provider, baseUrl: p.baseUrl, model: p.model, visionModel: "" });
  }

  return (
    <Card title={<span className="flex items-center gap-2"><Sparkles className="size-4" />AI</span>}
      action={s.configured && <span className="text-xs font-medium text-accent">On</span>}>
      <p className="mb-3 text-sm text-muted">
        Reads receipts and typed lists into your inventory, and suggests meals from what you have. Use any provider; the key stays on this server and is only sent to the service you pick.
      </p>
      <form className="flex flex-col gap-3" onSubmit={(e) => { e.preventDefault(); save.mutate({}); }}>
        <div className="flex flex-wrap gap-1.5">
          {AI_PRESETS.map((p) => (
            <button type="button" key={p.id} onClick={() => choose(p.id)}
              className={cx("min-h-9 rounded-xl border px-3 text-sm", presetId === p.id ? "border-accent bg-accent-soft font-medium text-accent" : "border-border")}>
              {p.label}
            </button>
          ))}
        </div>
        <p className="-mt-1 text-xs text-muted">{preset.hint}</p>

        <Field label="API key" error={err?.field("apiKey")}
          hint={s.hasApiKey ? "Saved. Type a new one to replace it." : preset.keyUrl ? <>Get one at <a className="underline" href={preset.keyUrl} target="_blank" rel="noreferrer">{new URL(preset.keyUrl).host}</a></> : "Leave blank if the service doesn't need one."}>
          <Input type="password" value={f.apiKey} onChange={(e) => set("apiKey", e.target.value)} autoComplete="off" placeholder={s.hasApiKey ? "••••••••" : preset.needsKey ? "sk-…" : "(none)"} />
        </Field>
        <Field label="Model" error={err?.field("model")}>
          <Input value={f.model} onChange={(e) => set("model", e.target.value)} autoCapitalize="none" spellCheck={false} placeholder="model id" />
        </Field>

        {advanced ? (
          <div className="grid gap-3">
            <Field label="Format">
              <Select value={f.provider} onChange={(e) => set("provider", e.target.value as AiProviderKind)}>
                <option value="openAiCompatible">OpenAI-compatible (most services)</option>
                <option value="anthropic">Anthropic (Claude)</option>
              </Select>
            </Field>
            <Field label="Base URL" error={err?.field("baseUrl")} hint={f.provider === "openAiCompatible" ? "The part before /chat/completions." : undefined}>
              <Input value={f.baseUrl} onChange={(e) => set("baseUrl", e.target.value)} inputMode="url" autoCapitalize="none" spellCheck={false} />
            </Field>
            <Field label="Photo model (optional)" hint="Only if your main model can't read images.">
              <Input value={f.visionModel} onChange={(e) => set("visionModel", e.target.value)} autoCapitalize="none" spellCheck={false} />
            </Field>
            {s.hasApiKey && <button type="button" className="self-start text-xs text-danger" onClick={() => save.mutate({ clearKey: true })}>Remove saved key</button>}
          </div>
        ) : (
          <button type="button" className="self-start text-xs text-muted underline" onClick={() => setAdvanced(true)}>Advanced</button>
        )}

        {test.data && (
          <div className="rounded-xl bg-surface-2 p-3 text-sm">
            <p className="flex items-center gap-1.5 text-accent"><Check className="size-4" />Connected to {test.data.model}</p>
            {test.data.vision
              ? <p className="mt-1 flex items-center gap-1.5 text-accent"><Check className="size-4" />Can read photos</p>
              : <p className="mt-1 text-warn">Can't read photos: {test.data.visionError} Typed lists will still work; for receipts, set a photo model under Advanced.</p>}
          </div>
        )}
        {test.error && !err?.errors && <p className="text-sm text-danger">{errorMessage(test.error)}</p>}
        {save.error && !err?.errors && <ErrorText error={save.error} />}

        <div className="flex flex-wrap gap-2">
          <Button variant="primary" busy={save.isPending} disabled={!f.model.trim() || !f.baseUrl.trim()}>Save</Button>
          <Button type="button" busy={test.isPending} disabled={!f.model.trim() || !f.baseUrl.trim()} onClick={() => test.mutate()}>Test connection</Button>
          {s.configured && <Button type="button" variant="ghost" busy={remove.isPending} onClick={() => remove.mutate()}>Turn off</Button>}
        </div>
      </form>
    </Card>
  );
}
