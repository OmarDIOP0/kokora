// Équipes suivies : stockées dans le navigateur, et synchronisées avec le compte quand on est connecté (account.js).
import { load, save } from './storage.js';

const KEY = 'k-favs';

function persist(ids) {
  save(KEY, ids);
  // Cookie lu par le serveur pour afficher les équipes suivies en premier.
  document.cookie = `k-favs=${ids.join('.')};path=/;max-age=31536000;samesite=lax`;
}

export const favorites = {
  ids: load(KEY, []),

  has(id) {
    return this.ids.includes(Number(id));
  },

  toggle(id) {
    id = Number(id);
    this.ids = this.has(id) ? this.ids.filter((x) => x !== id) : [...this.ids, id];
    persist(this.ids);
    document.dispatchEvent(new CustomEvent('favorites:changed', { detail: this.ids }));
  },

  /** Remplace la liste (venue du compte) sans la renvoyer au serveur. */
  set(ids) {
    this.ids = [...new Set(ids.map(Number))];
    persist(this.ids);
    document.dispatchEvent(new CustomEvent('favorites:synced', { detail: this.ids }));
  },
};
