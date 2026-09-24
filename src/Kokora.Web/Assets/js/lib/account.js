// Synchronisation des équipes suivies avec le compte connecté.
// Première visite d'un compte sur cet appareil : les équipes de l'appareil s'ajoutent à celles du compte.
// Ensuite, le compte fait foi et chaque « Suivre » y est enregistré.

const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content ?? '';
const post = (url, body) => fetch(url, {
  method: 'POST', headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf() }, body: JSON.stringify(body),
});
const FLAG = 'k-favs-user';

export async function syncAccount(store) {
  const user = document.querySelector('meta[name="k-user"]')?.content;
  if (!user) return;
  let first = true;
  try { first = localStorage.getItem(FLAG) !== user; } catch { /* stockage indisponible */ }
  try {
    const res = first ? await post('/compte/favoris', { ids: store.ids, merge: true }) : await fetch('/compte/favoris');
    if (res.ok && !res.redirected) {
      const ids = await res.json();
      if (ids.join('.') !== store.ids.join('.')) store.set(ids);
      try { localStorage.setItem(FLAG, user); } catch { /* ignoré */ }
    }
  } catch { /* hors ligne : on réessaiera à la prochaine page */ }
  document.addEventListener('favorites:changed', (e) => { post('/compte/favoris', { ids: e.detail, merge: false }).catch(() => {}); });
}
