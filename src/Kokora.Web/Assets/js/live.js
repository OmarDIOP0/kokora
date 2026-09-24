// Temps réel public (SignalR), chargé seulement si la page affiche un match en cours ou imminent ([data-watch]).
// Le serveur envoie l'état d'un match à chaque changement ; la page recharge uniquement les fragments concernés.
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

const pending = new Map(); // matchId → minuteur (regroupe les rafales de mises à jour)

async function refreshRow(el) {
  const res = await fetch(`/matchs/${el.dataset.match}/ligne${el.dataset.wide ? '?large=true' : ''}`, { headers: { 'HX-Request': 'true' } });
  if (!res.ok) return;
  const tpl = document.createElement('template');
  tpl.innerHTML = (await res.text()).trim();
  const fresh = tpl.content.firstElementChild;
  if (!fresh) return;
  const scoreChanged = el.querySelector('.match-scores')?.textContent.replace(/\s/g, '') !== fresh.querySelector('.match-scores')?.textContent.replace(/\s/g, '');
  el.replaceWith(fresh);
  if (scoreChanged) fresh.classList.add('is-updated');
}

function refreshMatch(id) {
  document.querySelectorAll(`[data-match="${id}"]`).forEach(refreshRow);
  if (document.querySelector(`[data-live-page="${id}"]`) && window.htmx) {
    window.htmx.ajax('GET', `/matchs/${id}/direct`, { target: '#match-head', swap: 'outerHTML' });
  }
}

function onUpdate(update) {
  const id = update.matchId;
  const block = document.getElementById('live-block');
  if (block && window.htmx) window.htmx.trigger(block, 'refresh');
  if (!document.querySelector(`[data-match="${id}"], [data-live-page="${id}"]`)) return;
  clearTimeout(pending.get(id));
  pending.set(id, setTimeout(() => { pending.delete(id); refreshMatch(id); }, 250));
}

/** Après une coupure, des mises à jour ont pu être manquées : tout ce qui est suivi est rechargé. */
function refreshAll() {
  const ids = new Set([...document.querySelectorAll('[data-watch][data-match], [data-live-page]')]
    .map((el) => el.dataset.match ?? el.dataset.livePage));
  ids.forEach(refreshMatch);
  const block = document.getElementById('live-block');
  if (block && window.htmx) window.htmx.trigger(block, 'refresh');
}

const connection = new HubConnectionBuilder()
  .withUrl('/direct/hub')
  .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000, 60000])
  .configureLogging(LogLevel.None)
  .build();

connection.on('match', onUpdate);
connection.onreconnecting(() => { window.kLive = false; });
connection.onreconnected(() => { window.kLive = true; refreshAll(); });
connection.onclose(() => { window.kLive = false; });

async function start() {
  try {
    await connection.start();
    window.kLive = true;
  } catch {
    window.kLive = false; // les rafraîchissements périodiques (htmx) prennent le relais
    setTimeout(start, 30000);
  }
}
start();

// Onglet revenu au premier plan après une longue veille (téléphone) : on resynchronise.
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'visible' && connection.state === 'Connected') refreshAll();
});
