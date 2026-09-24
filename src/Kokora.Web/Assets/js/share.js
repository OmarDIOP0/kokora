// Partage d'une carte de score en image (PNG 1080×1080), chargé au premier clic sur « Image ».
import { toBlob } from 'html-to-image';

export async function shareImage(button) {
  const card = document.getElementById(button.dataset.shareImage);
  if (!card) return;
  const label = button.querySelector('[data-label]');
  const original = label?.textContent;
  if (label) label.textContent = 'Préparation…';
  button.disabled = true;
  try {
    await document.fonts?.ready;
    // Délai maximal : la génération attend l'affichage de la page (onglet en arrière-plan, économie d'énergie).
    const blob = await Promise.race([
      toBlob(card, { width: 1080, height: 1080, pixelRatio: 1, cacheBust: true }),
      new Promise((_, reject) => setTimeout(() => reject(new Error('délai dépassé')), 20000)),
    ]);
    if (!blob) throw new Error('image');
    const file = new File([blob], `${button.dataset.fileName || 'kokora'}.png`, { type: 'image/png' });
    if (navigator.canShare?.({ files: [file] })) {
      await navigator.share({ files: [file], text: button.dataset.shareText || '' }).catch(() => {});
    } else {
      // Ordinateur ou navigateur sans partage de fichiers : téléchargement.
      const url = URL.createObjectURL(blob);
      const a = Object.assign(document.createElement('a'), { href: url, download: file.name });
      document.body.append(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 5000);
    }
  } catch {
    if (label) label.textContent = 'Échec, réessayer';
    button.disabled = false;
    return;
  }
  if (label) label.textContent = original;
  button.disabled = false;
}
