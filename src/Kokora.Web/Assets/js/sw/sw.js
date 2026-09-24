// Service worker Kokora (Workbox) : hors ligne et notifications push.
// Pensé pour la 3G : pages « réseau d'abord » avec délai court puis copie en cache, ressources du site en cache,
// images téléversées en cache limité. L'administration, le compte et le temps réel ne sont jamais mis en cache.
import { precacheAndRoute, cleanupOutdatedCaches, matchPrecache } from 'workbox-precaching';
import { registerRoute, setCatchHandler } from 'workbox-routing';
import { NetworkFirst, CacheFirst } from 'workbox-strategies';
import { ExpirationPlugin } from 'workbox-expiration';
import { CacheableResponsePlugin } from 'workbox-cacheable-response';

// Liste injectée au build (Assets/build.mjs) : CSS, JS communs, polices, icônes, page hors ligne.
precacheAndRoute(self.__WB_MANIFEST, { ignoreURLParametersMatching: [/^v$/, /^utm_/, /^source$/] });
cleanupOutdatedCaches();

const neverCached = (url) =>
  /^\/(admin|compte|direct|notifications|_snapshots|sante)(\/|$)/.test(url.pathname) || /\/(direct|ligne)$/.test(url.pathname);

// Pages : réseau d'abord ; au-delà de 4 s (réseau lent) ou hors ligne, la dernière version consultée.
registerRoute(
  ({ request, url }) => request.mode === 'navigate' && url.origin === self.location.origin && !neverCached(url),
  new NetworkFirst({
    cacheName: 'pages',
    networkTimeoutSeconds: 4,
    plugins: [
      new CacheableResponsePlugin({ statuses: [200] }),
      new ExpirationPlugin({ maxEntries: 60, maxAgeSeconds: 7 * 24 * 3600 }),
    ],
  }),
);

// Ressources versionnées du site : cache d'abord (leur adresse change à chaque nouvelle version).
registerRoute(
  ({ url }) => url.origin === self.location.origin && /^\/(dist|fonts|icons)\//.test(url.pathname),
  new CacheFirst({ cacheName: 'static', plugins: [new ExpirationPlugin({ maxEntries: 80, maxAgeSeconds: 365 * 24 * 3600 })] }),
);

// Logos, photos : cache d'abord, nombre limité (le stockage des téléphones est souvent plein).
registerRoute(
  ({ url }) => url.origin === self.location.origin && url.pathname.startsWith('/uploads/'),
  new CacheFirst({
    cacheName: 'images',
    plugins: [
      new CacheableResponsePlugin({ statuses: [0, 200] }),
      new ExpirationPlugin({ maxEntries: 200, maxAgeSeconds: 30 * 24 * 3600, purgeOnQuotaError: true }),
    ],
  }),
);

// Page jamais visitée et pas de réseau : page « hors ligne ».
setCatchHandler(async ({ request }) => {
  if (request.destination === 'document') return (await matchPrecache('/hors-ligne')) ?? Response.error();
  return Response.error();
});

// Nouvelle version : activée quand l'utilisateur accepte de recharger (voir lib/pwa.js).
self.addEventListener('message', (event) => {
  if (event.data?.type === 'SKIP_WAITING') self.skipWaiting();
});
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

// ---------------------------------------------------------------- Notifications push

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
