// Mode terrain (admin) : saisie en direct depuis un téléphone au bord du terrain.
// Chaque action part dans une file d'attente conservée sur l'appareil : si le réseau coupe, rien n'est perdu,
// l'envoi reprend tout seul. Une clé unique par action évite les doublons côté serveur.
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { syncClock, serverNow, minuteLabel, stopwatch } from './lib/clock.js';

const T = { goal: 1, penalty: 2, ownGoal: 3, missed: 4, yellow: 10, secondYellow: 11, red: 12, sub: 20, kick: 40 };
const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content ?? '';
const toast = (m, t) => window.toast?.(m, t);
const newKey = () => (crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`);

function storage(id) {
  const key = `k-live-q-${id}`;
  return {
    load() { try { return JSON.parse(localStorage.getItem(key) ?? '[]'); } catch { return []; } },
    save(q) { try { localStorage.setItem(key, JSON.stringify(q)); } catch { /* stockage indisponible */ } },
  };
}

function fieldMode(initial) {
  const store = storage(initial.id);
  return {
    s: initial,
    queue: store.load(),
    tick: 0,
    online: navigator.onLine,
    connected: false,
    busy: false,
    sheet: null,
    goalTypes: [
      { type: T.goal, label: 'But' }, { type: T.penalty, label: 'Penalty' },
      { type: T.ownGoal, label: 'CSC' }, { type: T.missed, label: 'Pen. raté' },
    ],

    init() {
      setInterval(() => { this.tick++; }, 1000);
      window.addEventListener('online', () => { this.online = true; this.flush(); });
      window.addEventListener('offline', () => { this.online = false; });
      this.flush();
      this.keepAwake();
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') { this.keepAwake(); this.refresh(); }
      });
      this.connect();
    },

    get live() { return this.s.status === 3 || this.s.status === 4; },
    get watch() {
      this.tick; // dépendance : recalcul chaque seconde
      return stopwatch(this.s.period, this.s.periodStartedAt, this.s.halfMinutes, this.s.extraHalfMinutes) ?? '';
    },
    currentMinute() {
      return minuteLabel(this.s.period, this.s.periodStartedAt, this.s.halfMinutes, this.s.extraHalfMinutes, serverNow())
        .replace(/[^0-9+]/g, '') || '';
    },
    team(side) { return side === 'home' ? this.s.home : this.s.away; },
    clubId(side) { return this.team(side).id; },

    // ---------------------------------------------------------------- File d'attente

    enqueue(url, body, label) {
      const key = newKey();
      if (url === 'actions') body.clientKey = key;
      this.queue.push({ url, body, label, key, at: Date.now() });
      store.save(this.queue);
      this.flush();
    },

    async flush() {
      if (this.busy || !this.queue.length) return;
      this.busy = true;
      try {
        while (this.queue.length) {
          const item = this.queue[0];
          let res;
          try {
            res = await fetch(`/admin/direct/${this.s.id}/${item.url}`, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf() },
              body: JSON.stringify(item.body ?? {}),
            });
          } catch {
            setTimeout(() => this.flush(), 5000); // réseau coupé : nouvel essai
            return;
          }
          syncClock(res);
          if (res.status === 401 || res.status === 403 || res.redirected) {
            toast('Session expirée : reconnectez-vous (les actions restent en attente).', 'error');
            return;
          }
          if (res.ok) {
            this.s = await res.json();
          } else if (res.status === 409) {
            await new Promise((r) => setTimeout(r, 800));
            continue; // conflit entre deux appareils : on renvoie
          } else if (res.status >= 500) {
            setTimeout(() => this.flush(), 5000);
            return;
          } else {
            const data = await res.json().catch(() => ({}));
            toast(`${item.label} : ${data.error ?? 'action refusée.'}`, 'error');
          }
          this.queue.shift();
          store.save(this.queue);
        }
      } finally {
        this.busy = false;
      }
    },

    async refresh() {
      if (this.queue.length) return this.flush();
      try {
        const res = await fetch(`/admin/direct/${this.s.id}/etat`, { headers: { Accept: 'application/json' } });
        syncClock(res);
        if (res.ok) this.s = await res.json();
      } catch { /* hors ligne */ }
    },

    connect() {
      const connection = new HubConnectionBuilder().withUrl('/direct/hub')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000]).configureLogging(LogLevel.None).build();
      // Un autre appareil (ou la saisie du résultat) a modifié ce match.
      connection.on('match', (u) => { if (u.matchId === this.s.id && !this.busy) this.refresh(); });
      connection.onreconnecting(() => { this.connected = false; });
      connection.onreconnected(() => { this.connected = true; this.refresh(); });
      connection.onclose(() => { this.connected = false; });
      const start = () => connection.start().then(() => { this.connected = true; }).catch(() => setTimeout(start, 10000));
      start();
    },

    async keepAwake() {
      try { await navigator.wakeLock?.request('screen'); } catch { /* non pris en charge */ }
    },

    // ---------------------------------------------------------------- Actions

    period(a) {
      if (a.to === 9 && !window.confirm('Terminer le match ? Le résultat sera enregistré et les classements mis à jour.')) return;
      this.enqueue('periode', { to: a.to }, a.label);
    },

    open(kind, side) {
      this.sheet = { kind, side, step: kind === 'sub' ? 'out' : 'player', goalType: T.goal, minute: this.currentMinute(), playerId: null, outId: null };
    },

    sheetTitle() {
      const sh = this.sheet;
      const name = this.team(sh.side).shortName;
      if (sh.kind === 'goal') return sh.step === 'assist' ? 'Passeur décisif' : `But · ${name}`;
      if (sh.kind === 'yellow') return `Carton jaune · ${name}`;
      if (sh.kind === 'red') return `Carton rouge · ${name}`;
      return sh.step === 'out' ? `Remplacement · ${name}` : 'Joueur entrant';
    },
    sheetHint() {
      const sh = this.sheet;
      if (sh.kind === 'goal' && sh.step === 'player')
        return sh.goalType === T.ownGoal ? `Joueur adverse qui a marqué contre son camp` : sh.goalType === T.missed ? 'Tireur' : 'Buteur';
      if (sh.step === 'assist') return 'Facultatif';
      if (sh.kind === 'sub') return sh.step === 'out' ? 'Joueur sortant' : 'Joueur entrant';
      return 'Joueur sanctionné (un 2e jaune devient rouge automatiquement)';
    },
    sheetPlayers() {
      const sh = this.sheet;
      if (!sh) return [];
      const other = sh.side === 'home' ? 'away' : 'home';
      const side = sh.kind === 'goal' && sh.goalType === T.ownGoal && sh.step === 'player' ? other : sh.side;
      return side === 'home' ? this.s.homeSquad : this.s.awaySquad;
    },

    parseMinute(text) {
      const m = String(text ?? '').match(/^\s*(\d{1,3})(?:\s*\+\s*(\d{1,2}))?\s*$/);
      return m ? { minute: Number(m[1]), addedTime: m[2] ? Number(m[2]) : null } : { minute: null, addedTime: null };
    },

    pick(playerId) {
      const sh = this.sheet;
      if (sh.kind === 'sub' && sh.step === 'out') { sh.outId = playerId; sh.step = 'in'; return; }
      if (sh.kind === 'goal' && sh.step === 'player' && (sh.goalType === T.goal || sh.goalType === T.penalty) && playerId !== null
          && this.sheetPlayers().length > 1) {
        sh.playerId = playerId; sh.step = 'assist'; return;
      }
      if (sh.step === 'assist') this.submit(sh.playerId, playerId);
      else this.submit(playerId, null);
    },

    submit(playerId, assistId) {
      const sh = this.sheet;
      const type = sh.kind === 'goal' ? sh.goalType : sh.kind === 'yellow' ? T.yellow : sh.kind === 'red' ? T.red : T.sub;
      const { minute, addedTime } = this.parseMinute(sh.minute);
      const body = {
        type, clubId: this.clubId(sh.side), playerId, assistPlayerId: assistId,
        playerOutId: sh.kind === 'sub' ? sh.outId : null, minute, addedTime,
      };
      const labels = { 1: 'But', 2: 'But sur penalty', 3: 'But contre son camp', 4: 'Penalty manqué', 10: 'Carton jaune', 12: 'Carton rouge', 20: 'Remplacement' };
      this.sheet = null;
      this.enqueue('actions', body, `${labels[type]} (${this.team(sh.side).shortName})`);
    },

    kick(side, scored) {
      this.enqueue('actions', { type: T.kick, clubId: this.clubId(side), isScored: scored }, `Tir au but (${this.team(side).shortName})`);
    },

    cancel(e) {
      if (!window.confirm(`Annuler « ${this.tag(e)}${e.player ? ' · ' + e.player : ''} » ?`)) return;
      this.enqueue(`actions/${e.id}/annuler`, {}, 'Annulation');
    },

    fixMinute() {
      const value = window.prompt('Minute actuelle annoncée par l’arbitre :', this.currentMinute().split('+')[0]);
      const minute = Number(value);
      if (value && Number.isInteger(minute) && minute > 0) this.enqueue('minute', { minute }, 'Correction de la minute');
    },

    // ---------------------------------------------------------------- Affichage

    tag(e) {
      return ({ 1: 'But', 2: 'Pen.', 3: 'CSC', 4: 'Pen. raté', 10: 'Jaune', 11: '2e jaune', 12: 'Rouge', 20: 'Rempl.' })[e.type]
        ?? (e.isScored ? 'TAB ✓' : 'TAB ✗');
    },
    tagClass(e) {
      if ([1, 2, 3].includes(e.type)) return 'is-goal';
      if (e.type === 10) return 'is-yellow';
      if (e.type === 11 || e.type === 12) return 'is-red';
      return '';
    },
    detail(e) {
      const team = (e.isHome ? this.s.home : this.s.away).shortName;
      if (e.type === 20) return `${team} · sort : ${e.playerOut ?? '?'}`;
      if (e.assist) return `${team} · passe : ${e.assist}`;
      return team;
    },
  };
}

// Alpine est déjà démarré (core.js) : on enregistre le composant puis on remplace la zone marquée x-ignore
// par une copie active ; l'observateur d'Alpine l'initialise alors une seule fois.
const root = document.querySelector('[data-field-mode]');
if (root && window.Alpine) {
  const initial = JSON.parse(document.getElementById('field-state').textContent);
  window.Alpine.data('fieldMode', () => fieldMode(initial));
  const fresh = root.cloneNode(true);
  fresh.removeAttribute('x-ignore');
  fresh.setAttribute('x-data', 'fieldMode');
  root.replaceWith(fresh);
}
