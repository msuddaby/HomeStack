// Notifications for the installed app. Loaded into the generated service worker (workbox importScripts).
// Messages are { title, body, url, tag, urgent }, encrypted by the server for this device.

self.addEventListener("push", (event) => {
  let m = {};
  try { m = event.data ? event.data.json() : {}; } catch { m = { body: event.data ? event.data.text() : "" }; }
  event.waitUntil(self.registration.showNotification(m.title || "HomeStack", {
    body: m.body || "",
    icon: "/pwa-192x192.png",
    // A newer message with the same tag (today's reminder) replaces the old one instead of stacking.
    tag: m.tag || undefined,
    renotify: !!m.tag,
    requireInteraction: !!m.urgent,
    data: { url: m.url || "/" },
  }));
});

// Tapping a notification opens its page: in the app if it's already open, else in a new window.
self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const url = new URL(event.notification.data?.url || "/", self.location.origin).href;
  event.waitUntil((async () => {
    const open = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
    const client = open.find((c) => new URL(c.url).origin === self.location.origin);
    if (client) {
      await client.focus();
      if (client.url !== url && "navigate" in client) await client.navigate(url).catch(() => {});
      return;
    }
    await self.clients.openWindow(url);
  })());
});

// The browser replaced this device's subscription (keys expired): tell the server about the new one.
self.addEventListener("pushsubscriptionchange", (event) => {
  event.waitUntil((async () => {
    const key = event.oldSubscription?.options?.applicationServerKey;
    const sub = event.newSubscription ?? (key ? await self.registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key }) : null);
    if (!sub) return;
    const json = sub.toJSON();
    await fetch("/api/notifications/push/subscriptions", {
      method: "POST",
      credentials: "same-origin",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth, appUrl: self.location.origin }),
    });
  })());
});
