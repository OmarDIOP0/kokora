// Page /design : bascule entre les palettes proposées.
const root = document.documentElement;

function setPalette(name) {
  if (name === 'pelouse') delete root.dataset.palette; else root.dataset.palette = name;
  try { localStorage.setItem('k-palette', name); } catch { /* ignoré */ }
  document.querySelectorAll('[data-set-palette]').forEach((b) =>
    b.setAttribute('aria-pressed', String(b.dataset.setPalette === name)));
  renderSwatchValues();
}

function renderSwatchValues() {
  const styles = getComputedStyle(root);
  document.querySelectorAll('[data-token]').forEach((el) => {
    el.textContent = styles.getPropertyValue(el.dataset.token).trim();
  });
}

document.addEventListener('click', (e) => {
  const btn = e.target.closest('[data-set-palette]');
  if (btn) setPalette(btn.dataset.setPalette);
  if (e.target.closest('[data-theme-toggle]')) setTimeout(renderSwatchValues, 0);
});

document.addEventListener('DOMContentLoaded', () => {
  let current = 'pelouse';
  try { current = localStorage.getItem('k-palette') ?? 'pelouse'; } catch { /* ignoré */ }
  setPalette(current);
});
