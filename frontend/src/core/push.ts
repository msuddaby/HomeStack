import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { api } from "./api";

export interface PushDevice { id: number; device: string | null; endpoint: string; createdAt: string; lastSentAt: string | null; lastError: string | null }
interface PushStatus { publicKey: string; devices: PushDevice[] }

/**
 * Where this device stands with notifications:
 * - unsupported: the browser can't do Web Push (or there's no service worker, as in development)
 * - install: iPhone/iPad in Safari; notifications only work from the Home Screen app
 * - blocked: the user said no; only the browser/OS settings can undo that
 * - off / on
 */
export type PushState = "loading" | "unsupported" | "install" | "blocked" | "off" | "on";

const PUSH_KEY = ["push"];

const isIos = () => /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
const isStandalone = () => matchMedia("(display-mode: standalone)").matches || (navigator as { standalone?: boolean }).standalone === true;
const supported = () => "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;

/** The service worker, or null if there isn't one within a few seconds (development, or blocked). */
async function registration(): Promise<ServiceWorkerRegistration | null> {
  if (!("serviceWorker" in navigator)) return null;
  return Promise.race([navigator.serviceWorker.ready, new Promise<null>((r) => setTimeout(() => r(null), 4000))]);
}

/** "iPhone", "Android · Chrome", "Mac · Safari": enough to tell your devices apart. */
function deviceName() {
  const ua = navigator.userAgent;
  const os = /iPhone/.test(ua) ? "iPhone" : /iPad/.test(ua) || isIos() ? "iPad" : /Android/.test(ua) ? "Android"
    : /Mac/.test(ua) ? "Mac" : /Windows/.test(ua) ? "Windows" : /Linux/.test(ua) ? "Linux" : "Device";
  if (os === "iPhone" || os === "iPad") return os;
  const browser = /Edg\//.test(ua) ? "Edge" : /Firefox\//.test(ua) ? "Firefox" : /Chrome\//.test(ua) ? "Chrome" : /Safari\//.test(ua) ? "Safari" : null;
  return browser ? `${os} · ${browser}` : os;
}

function keyBytes(base64url: string) {
  const s = atob(base64url.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(base64url.length / 4) * 4, "="));
  return Uint8Array.from(s, (c) => c.charCodeAt(0));
}

function sameBytes(a: ArrayBuffer | null, b: Uint8Array) {
  if (!a || a.byteLength !== b.length) return false;
  const x = new Uint8Array(a);
  return x.every((v, i) => v === b[i]);
}

const send = (sub: PushSubscription) => {
  const j = sub.toJSON();
  return api<PushDevice>("/notifications/push/subscriptions", {
    body: { endpoint: j.endpoint, p256dh: j.keys?.p256dh, auth: j.keys?.auth, device: deviceName(), appUrl: location.origin },
  });
};

/** Notifications on this device: its state, turning them on and off, and the member's other devices. */
export function usePush() {
  const qc = useQueryClient();
  const status = useQuery({ queryKey: PUSH_KEY, queryFn: () => api<PushStatus>("/notifications/push") });
  const [endpoint, setEndpoint] = useState<string | null | undefined>(undefined);
  const [state, setState] = useState<PushState>("loading");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!supported()) return setState(isIos() && !isStandalone() ? "install" : "unsupported");
      const reg = await registration();
      if (cancelled) return;
      if (!reg) return setState("unsupported");
      const sub = await reg.pushManager.getSubscription();
      if (cancelled) return;
      setEndpoint(sub?.endpoint ?? null);
      if (Notification.permission === "denied") return setState("blocked");
      setState(sub ? "on" : "off");
      // Make sure the server still has this device (e.g. after a restore, or a removal from another device).
      if (sub) send(sub).then(() => qc.invalidateQueries({ queryKey: PUSH_KEY })).catch(() => {});
    })();
    return () => { cancelled = true; };
  }, [qc]);

  const turnOn = useMutation({
    mutationFn: async () => {
      // Ask straight away: Safari only shows the prompt in direct response to a tap.
      const permission = await Notification.requestPermission();
      if (permission !== "granted") { setState(permission === "denied" ? "blocked" : "off"); throw new Error("Notifications weren't allowed."); }
      const key = status.data?.publicKey ?? (await api<PushStatus>("/notifications/push")).publicKey;
      const reg = await registration();
      if (!reg) throw new Error("The app isn't ready for notifications yet. Reload and try again.");
      let sub = await reg.pushManager.getSubscription();
      // Subscribed with another server key (e.g. the server's data was reset): start over.
      if (sub && !sameBytes(sub.options.applicationServerKey, keyBytes(key))) {
        await sub.unsubscribe();
        sub = null;
      }
      sub ??= await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(key) });
      await send(sub);
      setEndpoint(sub.endpoint);
      setState("on");
    },
    onSettled: () => qc.invalidateQueries({ queryKey: PUSH_KEY }),
  });

  const turnOff = useMutation({
    mutationFn: async () => {
      const sub = await (await registration())?.pushManager.getSubscription();
      if (sub) {
        await api("/notifications/push/unsubscribe", { body: { endpoint: sub.endpoint } });
        await sub.unsubscribe();
      }
      setEndpoint(null);
      setState("off");
    },
    onSettled: () => qc.invalidateQueries({ queryKey: PUSH_KEY }),
  });

  const remove = useMutation({
    mutationFn: (id: number) => api(`/notifications/push/subscriptions/${id}`, { method: "DELETE" }),
    onSettled: () => qc.invalidateQueries({ queryKey: PUSH_KEY }),
  });

  const devices = status.data?.devices ?? [];
  return {
    state, turnOn, turnOff, remove,
    thisDevice: devices.find((d) => d.endpoint === endpoint) ?? null,
    otherDevices: devices.filter((d) => d.endpoint !== endpoint),
  };
}
