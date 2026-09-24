// Bundle commun à toutes les pages publiques : htmx + Alpine + Day.js + petits comportements.
import htmx from 'htmx.org';
import Alpine from 'alpinejs';
import { dayjs } from './lib/time.js';
import { favorites } from './lib/favorites.js';
import { theme } from './lib/theme.js';
import { tickClocks } from './lib/clock.js';

window.htmx = htmx;
htmx.config.defaultSwapStyle = 'innerHTML';
htmx.config.scrollIntoViewOnBoost = false;
htmx.config.historyCacheSize = 5;
htmx.config.globalViewTransitions = false;

// Jeton anti-CSRF ajouté à toutes les requêtes htmx non-GET.
document.addEventListener('htmx:configRequest', (e) => {
  const token = document.querySelector('meta[name="csrf-token"]')?.content;
  if (token && e.detail.verb !== 'get') e.detail.headers['RequestVerificationToken'] = token;
});

Alpine.store('favs', favorites);
Alpine.store('theme', theme);
window.Alpine = Alpine;

// Heures relatives : <time data-rel datetime="…">
function refreshRelativeTimes(root = document) {
  root.querySelectorAll('time[data-rel]').forEach((el) => {
    el.textContent = dayjs(el.getAttribute('datetime')).fromNow();
  });
}
// Compte à rebours : <span data-countdown="iso">
function refreshCountdowns(root = document) {
  root.querySelectorAll('[data-countdown]').forEach((el) => {
    const diff = dayjs(el.dataset.countdown).diff(dayjs(), 'second');
    if (diff <= 0) { el.textContent = 'Coup d’envoi'; return; }
    const d = Math.floor(diff / 86400), h = Math.floor((diff % 86400) / 3600),
          m = Math.floor((diff % 3600) / 60), s = diff % 60;
    const pad = (n) => String(n).padStart(2, '0');
    el.textContent = d > 0 ? `${d} j ${pad(h)} h ${pad(m)} min` : `${pad(h)}:${pad(m)}:${pad(s)}`;
  });
}

document.addEventListener('htmx:afterSwap', (e) => { refreshRelativeTimes(e.target); refreshCountdowns(e.target); tickClocks(); });
// Minute des matchs en direct, calculée localement entre deux mises à jour du serveur.
setInterval(tickClocks, 10_000);
setInterval(refreshRelativeTimes, 30_000);
setInterval(refreshCountdowns, 1_000);

// Indicateur hors ligne
function updateOnline() { document.documentElement.toggleAttribute('data-offline', !navigator.onLine); }
window.addEventListener('online', updateOnline);
window.addEventListener('offline', updateOnline);

document.addEventListener('DOMContentLoaded', () => {
  refreshRelativeTimes();
  refreshCountdowns();
  updateOnline();
  tickClocks();
  // Temps réel : uniquement si la page montre un match en cours ou sur le point de commencer.
  if (document.querySelector('[data-watch]')) import('./live.js');
  // Centre la date sélectionnée dans la bande de dates.
  document.querySelector('.datestrip [aria-current="date"]')?.scrollIntoView({ inline: 'center', block: 'nearest' });
});

Alpine.start();
