// Application installable : enregistrement du service worker, nouvelle version, bouton « Installer ».
import { Workbox } from 'workbox-window';

const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent);
const standalone = () => matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;

/** Store Alpine « pwa » : état de l'installation et de la mise à jour. */
export const pwa = {
  canInstall: false,
  installed: standalone(),
  iosHint: isIos && !standalone(),
  updateReady: false,
  _prompt: null,
  _wb: null,

  async install() {
    if (!this._prompt) return;
    this._prompt.prompt();
    const { outcome } = await this._prompt.userChoice;
    this._prompt = null;
    this.canInstall = false;
    if (outcome === 'accepted') this.installed = true;
  },

  /** Nouvelle version prête : on l'active puis la page se recharge. */
  reload() {
    if (!this._wb) return window.location.reload();
    this._wb.addEventListener('controlling', () => window.location.reload());
    this._wb.messageSkipWaiting();
  },
};

export function startPwa(store) {
  window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault(); // bouton « Installer » affiché dans la page Plus plutôt qu'une bannière imposée
    store._prompt = e;
    store.canInstall = true;
  });
  window.addEventListener('appinstalled', () => { store.installed = true; store.canInstall = false; });

  if (!('serviceWorker' in navigator)) return;
  // Enregistré après le chargement : la première visite (souvent en 3G) reste prioritaire.
  window.addEventListener('load', () => {
    const wb = new Workbox('/sw.js', { scope: '/' });
    store._wb = wb;
    wb.addEventListener('waiting', () => { store.updateReady = true; });
    wb.register().catch(() => { /* navigation privée, stockage indisponible… */ });
  });
}
