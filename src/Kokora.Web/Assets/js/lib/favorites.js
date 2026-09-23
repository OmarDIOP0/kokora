// Équipes suivies, stockées dans le navigateur (synchronisées avec le compte en phase 7).
import { load, save } from './storage.js';

const KEY = 'k-favs';

export const favorites = {
  ids: load(KEY, []),

  has(id) {
    return this.ids.includes(Number(id));
  },

  toggle(id) {
    id = Number(id);
    this.ids = this.has(id) ? this.ids.filter((x) => x !== id) : [...this.ids, id];
    save(KEY, this.ids);
    // Cookie lu par le serveur pour afficher les équipes suivies en premier.
    document.cookie = `k-favs=${this.ids.join('.')};path=/;max-age=31536000;samesite=lax`;
    document.dispatchEvent(new CustomEvent('favorites:changed', { detail: this.ids }));
  },
};
