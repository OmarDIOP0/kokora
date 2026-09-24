// Notifications push (Web Push standard) : abonnement de l'appareil, sans compte obligatoire.
// L'abonnement retient les équipes suivies ; il est mis à jour à chaque « Suivre ».

const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content ?? '';
const post = (url, body) => fetch(url, {
  method: 'POST', headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf() }, body: JSON.stringify(body),
});

export const pushSupported = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent);
const standalone = matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;

function keyBytes(base64) {
  const padded = (base64 + '='.repeat((4 - (base64.length % 4)) % 4)).replace(/-/g, '+').replace(/_/g, '/');
  return Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
}

async function currentSubscription() {
  if (!pushSupported) return null;
  const reg = await navigator.serviceWorker.getRegistration('/');
  return reg ? reg.pushManager.getSubscription() : null;
}

const favorites = () => window.Alpine?.store('favs')?.ids ?? [];

export async function enablePush(prefs) {
  const permission = await Notification.requestPermission();
  if (permission !== 'granted') throw new Error(permission === 'denied' ? 'denied' : 'dismissed');
  const res = await fetch('/notifications/cle');
  if (!res.ok) throw new Error('unavailable');
  const { key } = await res.json();
  const reg = await navigator.serviceWorker.register('/sw.js', { scope: '/' });
  await navigator.serviceWorker.ready;
  const sub = (await reg.pushManager.getSubscription())
    ?? await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(key) });
  const json = sub.toJSON();
  const saved = await post('/notifications/abonnement', {
    endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth, clubIds: favorites(), ...prefs,
  });
  if (!saved.ok) throw new Error('server');
}

export async function disablePush() {
  const sub = await currentSubscription();
  if (!sub) return;
  await post('/notifications/desabonnement', { endpoint: sub.endpoint });
  await sub.unsubscribe();
}

// « Suivre » une équipe (ou synchronisation avec le compte) : l'abonnement de l'appareil suit.
async function followClubs(e) {
  const sub = await currentSubscription().catch(() => null);
  if (sub) post('/notifications/equipes', { endpoint: sub.endpoint, clubIds: e.detail });
}
document.addEventListener('favorites:changed', followClubs);
document.addEventListener('favorites:synced', followClubs);

const defaults = { notifyKickoff: true, notifyGoals: true, notifyFullTime: true, notifyNews: true };

/** Composant Alpine des réglages de notifications (page Compte et page Plus). */
export function pushSettings() {
  return {
    state: 'loading', // unsupported | ios | denied | off | on
    prefs: { ...defaults },
    busy: false,
    error: '',
    async init() {
      if (!pushSupported) { this.state = isIos && !standalone ? 'ios' : 'unsupported'; return; }
      if (Notification.permission === 'denied') { this.state = 'denied'; return; }
      const sub = await currentSubscription().catch(() => null);
      if (!sub) { this.state = 'off'; return; }
      const res = await post('/notifications/preferences', { endpoint: sub.endpoint }).catch(() => null);
      if (res?.ok) {
        const p = await res.json();
        this.prefs = { notifyKickoff: p.notifyKickoff, notifyGoals: p.notifyGoals, notifyFullTime: p.notifyFullTime, notifyNews: p.notifyNews };
        this.state = 'on';
      } else {
        this.state = 'off'; // abonnement inconnu du serveur (ex. base réinitialisée) : à réactiver
      }
    },
    async enable() {
      this.busy = true; this.error = '';
      try { await enablePush(this.prefs); this.state = 'on'; }
      catch (err) {
        if (err.message === 'denied') this.state = 'denied';
        else this.error = err.message === 'dismissed' ? 'Autorisation non accordée.' : 'Activation impossible pour le moment.';
      } finally { this.busy = false; }
    },
    async disable() {
      this.busy = true;
      try { await disablePush(); this.state = 'off'; } finally { this.busy = false; }
    },
    async save() {
      const sub = await currentSubscription();
      if (!sub) return;
      const json = sub.toJSON();
      await post('/notifications/abonnement', {
        endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth, clubIds: favorites(), ...this.prefs,
      });
      window.toast?.('Préférences enregistrées');
    },
  };
}
