// Éditeur de texte des infos (Quill), chargé uniquement sur le formulaire de rédaction.
// Le HTML envoyé est de toute façon nettoyé côté serveur (liste blanche).
import Quill from 'quill';
import 'quill/dist/quill.snow.css';

const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content ?? '';

// Seuls ces formats sont conservés, y compris lors d'un collage (WhatsApp, Word, pages web).
const formats = ['header', 'bold', 'italic', 'underline', 'strike', 'list', 'blockquote', 'link', 'image'];

const titles = {
  'ql-bold': 'Gras', 'ql-italic': 'Italique', 'ql-underline': 'Souligné', 'ql-strike': 'Barré',
  'ql-blockquote': 'Citation', 'ql-link': 'Lien', 'ql-image': 'Insérer une image', 'ql-clean': 'Effacer la mise en forme',
};

function uploadImage(quill) {
  const input = document.createElement('input');
  input.type = 'file';
  input.accept = 'image/png,image/jpeg,image/webp';
  input.addEventListener('change', async () => {
    const file = input.files?.[0];
    if (!file) return;
    if (file.size > 8 * 1024 * 1024) { window.toast?.('Image trop lourde (8 Mo maximum).', 'error'); return; }
    const body = new FormData();
    body.append('file', file);
    window.toast?.('Envoi de l’image…');
    try {
      const res = await fetch('/admin/infos/images', { method: 'POST', body, headers: { RequestVerificationToken: csrf() } });
      const data = await res.json().catch(() => ({}));
      if (!res.ok || !data.url) throw new Error(data.error || 'Envoi impossible.');
      const range = quill.getSelection(true);
      quill.insertEmbed(range.index, 'image', data.url, 'user');
      quill.setSelection(range.index + 1, 0);
    } catch (err) {
      window.toast?.(err.message || 'Envoi impossible.', 'error');
    }
  });
  input.click();
}

function mount(el) {
  const hidden = document.getElementById(el.dataset.editor);
  const form = el.closest('form');
  const quill = new Quill(el, {
    theme: 'snow',
    formats,
    placeholder: 'Rédigez l’info…',
    modules: {
      toolbar: {
        container: [
          [{ header: [2, 3, false] }],
          ['bold', 'italic', 'underline'],
          [{ list: 'ordered' }, { list: 'bullet' }, 'blockquote'],
          ['link', 'image'],
          ['clean'],
        ],
        handlers: { image() { uploadImage(quill); } },
      },
    },
  });

  // Libellés français des boutons (accessibilité + infobulles).
  const toolbar = quill.getModule('toolbar').container;
  toolbar.setAttribute('aria-label', 'Mise en forme');
  toolbar.querySelectorAll('button').forEach((b) => {
    const cls = [...b.classList].find((c) => c.startsWith('ql-'));
    let title = titles[cls];
    if (cls === 'ql-list') title = b.value === 'ordered' ? 'Liste numérotée' : 'Liste à puces';
    if (title) { b.title = title; b.setAttribute('aria-label', title); }
  });

  let dirty = false;
  quill.on('text-change', () => { dirty = true; });
  form.addEventListener('input', () => { dirty = true; });
  form.addEventListener('change', () => { dirty = true; });

  form.addEventListener('submit', () => {
    // getSemanticHTML (Quill 2.0) remplace chaque espace par une insécable : on rétablit des espaces normales.
    hidden.value = quill.getText().trim().length === 0 && !quill.root.querySelector('img')
      ? '' : quill.getSemanticHTML().replace(/&nbsp;/g, ' ');
    dirty = false;
  });
  // Évite de perdre un texte en quittant la page sans enregistrer.
  window.addEventListener('beforeunload', (e) => { if (dirty) { e.preventDefault(); e.returnValue = ''; } });
}

document.querySelectorAll('[data-editor]').forEach(mount);
