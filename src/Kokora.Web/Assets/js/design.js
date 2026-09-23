// Page /design : affiche la valeur courante de chaque token de couleur.
function renderSwatchValues() {
  const styles = getComputedStyle(document.documentElement);
  document.querySelectorAll('[data-token]').forEach((el) => {
    el.textContent = styles.getPropertyValue(el.dataset.token).trim();
  });
}

document.addEventListener('click', (e) => {
  if (e.target.closest('[data-theme-toggle]')) setTimeout(renderSwatchValues, 0);
});
document.addEventListener('DOMContentLoaded', renderSwatchValues);
