// Minute d'un match en direct, calculée dans le navigateur (même règle que MatchClock côté serveur).
// Périodes : 1 = 1re, 2 = mi-temps, 3 = 2e, 4 = pause, 5/7 = prolongations, 6 = mi-temps des prolongations, 8 = tirs au but.

let offset = 0; // décalage horloge serveur - appareil (ms)

/** Recale l'horloge sur l'en-tête Date d'une réponse du serveur (téléphones mal réglés). */
export function syncClock(response) {
  const date = Date.parse(response?.headers?.get('Date') ?? '');
  if (!Number.isNaN(date)) offset = date - Date.now();
}

export const serverNow = () => Date.now() + offset;

export function minuteLabel(period, startedAt, half = 45, extra = 15, now = serverNow()) {
  const running = { 1: [0, half], 3: [half, half], 5: [2 * half, extra], 7: [2 * half + extra, extra] }[period];
  if (running) {
    const [base, length] = running;
    if (!startedAt) return String(base + 1);
    const elapsed = Math.max(1, Math.floor((now - Date.parse(startedAt)) / 60000) + 1);
    return elapsed > length ? `${base + length}+${elapsed - length}` : String(base + elapsed);
  }
  return { 2: 'MT', 6: 'MT', 4: 'Pause', 8: 'TAB' }[period] ?? '';
}

/** Chronomètre détaillé (mm:ss) pour l'écran terrain. */
export function stopwatch(period, startedAt, half = 45, extra = 15, now = serverNow()) {
  const base = { 1: 0, 3: half, 5: 2 * half, 7: 2 * half + extra }[period];
  if (base === undefined || !startedAt) return null;
  const seconds = Math.max(0, Math.floor((now - Date.parse(startedAt)) / 1000)) + base * 60;
  return `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
}

/** Met à jour tous les <… data-clock data-period data-started data-half data-extra> de la page. */
export function tickClocks(root = document) {
  root.querySelectorAll('[data-clock]').forEach((el) => {
    const d = el.dataset;
    const label = minuteLabel(Number(d.period), d.started || null, Number(d.half || 45), Number(d.extra || 15));
    if (label && el.textContent !== label) el.textContent = label;
  });
}
