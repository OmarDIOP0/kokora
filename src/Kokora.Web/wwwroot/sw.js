// Service worker Kokora : notifications push (le cache hors ligne s'ajoute en phase 8).
// Servi à la racine pour couvrir tout le site.

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

self.addEventListener('push', (event) => {
  let data = {};
  try { data = event.data ? event.data.json() : {}; } catch { data = { body: event.data?.text() }; }
  event.waitUntil(self.registration.showNotification(data.title || 'Kokora', {
    body: data.body || '',
    tag: data.tag,
    renotify: Boolean(data.tag), // un nouveau but remplace la notification du même match, avec vibration
    icon: '/icons/icon-192.png',
    badge: '/icons/badge-96.png',
    lang: 'fr',
    data: { url: data.url || '/' },
  }));
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const url = new URL(event.notification.data?.url || '/', self.location.origin).href;
  event.waitUntil((async () => {
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const same = windows.find((w) => w.url === url);
    if (same) return same.focus();
    const any = windows.find((w) => 'navigate' in w);
    if (any) { await any.navigate(url); return any.focus(); }
    return self.clients.openWindow(url);
  })());
});
