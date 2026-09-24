// Bundle de l'administration (chargé en plus de core.js) :
// dates (Flatpickr), listes avec recherche (Tom Select), glisser-déposer (SortableJS), notifications (Notyf).
import flatpickr from 'flatpickr';
import { French } from 'flatpickr/dist/l10n/fr.js';
import TomSelect from 'tom-select/base';
import TomRemoveButton from 'tom-select/plugins/remove_button/plugin.js';
import Sortable from 'sortablejs';
import { Notyf } from 'notyf';

import 'flatpickr/dist/flatpickr.min.css';
import 'tom-select/dist/css/tom-select.css';
import 'notyf/notyf.min.css';

TomSelect.define('remove_button', TomRemoveButton);
flatpickr.localize(French);

const notyf = new Notyf({
  duration: 3500,
  position: { x: 'center', y: 'bottom' },
  dismissible: true,
  types: [
    { type: 'success', background: '#1d2320', icon: false }, // neutre foncé, lisible dans les deux thèmes
    { type: 'error', background: '#b3261e', icon: false, duration: 6000 },
  ],
});
window.toast = (message, type = 'success') => notyf.open({ type, message });

function csrf() {
  return document.querySelector('meta[name="csrf-token"]')?.content ?? '';
}

function init(root = document) {
  root.querySelectorAll('input[data-datetime]:not(.flatpickr-input)').forEach((el) =>
    flatpickr(el, {
      enableTime: true, time_24hr: true, minuteIncrement: 5, dateFormat: 'Y-m-d H:i',
      altInput: true, altFormat: 'l j F Y, H\\hi', disableMobile: false, allowInput: false,
    }));
  root.querySelectorAll('input[data-date]:not(.flatpickr-input)').forEach((el) =>
    flatpickr(el, { dateFormat: 'Y-m-d', altInput: true, altFormat: 'l j F Y', allowInput: false }));

  root.querySelectorAll('select[data-tom]').forEach((el) => {
    if (el.tomselect) return;
    new TomSelect(el, {
      plugins: el.multiple ? ['remove_button'] : [],
      maxOptions: null,
      allowEmptyOption: true,
      hidePlaceholder: false,
      render: {
        no_results: () => '<div class="no-results">Aucun résultat</div>',
      },
    });
  });

  root.querySelectorAll('[data-sortable]').forEach((list) => {
    if (list._sortable) return;
    list._sortable = Sortable.create(list, {
      handle: '[data-handle]', animation: 150, ghostClass: 'is-ghost',
      onEnd: async () => {
        const url = list.dataset.sortable;
        if (!url) return;
        const body = new URLSearchParams();
        list.querySelectorAll(':scope > [data-id]').forEach((item) => body.append('ids', item.dataset.id));
        const res = await fetch(url, { method: 'POST', body, headers: { RequestVerificationToken: csrf() } });
        window.toast(res.ok ? 'Ordre enregistré' : 'Impossible d’enregistrer l’ordre', res.ok ? 'success' : 'error');
      },
    });
  });

  // Aperçu de la valeur hexadécimale d'un sélecteur de couleur.
  root.querySelectorAll('input[type="color"]').forEach((el) => {
    const label = root.querySelector(`[data-color-value="${el.id}"]`);
    if (label) el.addEventListener('input', () => { label.textContent = el.value.toUpperCase(); });
  });
}

// Confirmation avant une action destructive : <form data-confirm="…"> ou <button data-confirm="…">.
document.addEventListener('submit', (e) => {
  const form = e.target;
  const message = e.submitter?.dataset.confirm ?? form.dataset.confirm;
  if (message && !window.confirm(message)) e.preventDefault();
}, true);

document.addEventListener('htmx:afterSwap', (e) => init(e.target));
document.addEventListener('htmx:responseError', (e) => {
  const text = e.detail.xhr.responseText;
  window.toast(text && text.length < 300 ? text : 'Une erreur est survenue.', 'error');
});
document.addEventListener('htmx:sendError', () => window.toast('Connexion impossible. Vérifiez le réseau.', 'error'));

document.addEventListener('DOMContentLoaded', () => {
  init();
  const flash = document.body.dataset.flash;
  if (flash) {
    try {
      const { type, message } = JSON.parse(flash);
      window.toast(message, type);
    } catch { /* ignoré */ }
  }
});
