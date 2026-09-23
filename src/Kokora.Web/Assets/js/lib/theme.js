// Thème clair / sombre / système et mode économie de données.
// L'état initial est posé avant l'affichage par le script en ligne du <head>.
const root = document.documentElement;
const media = window.matchMedia('(prefers-color-scheme: dark)');

function read(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}
function write(key, value) {
  try { value === null ? localStorage.removeItem(key) : localStorage.setItem(key, value); } catch { /* ignoré */ }
}

function apply(mode) {
  const dark = mode === 'dark' || (mode === 'system' && media.matches);
  root.dataset.theme = dark ? 'dark' : 'light';
  document.querySelector('meta[name="theme-color"]')
    ?.setAttribute('content', getComputedStyle(root).getPropertyValue('--surface').trim());
}

export const theme = {
  mode: read('k-theme') ?? 'system',
  saver: read('k-saver') === '1',

  get isDark() { return root.dataset.theme === 'dark'; },

  set(mode) {
    this.mode = mode;
    write('k-theme', mode === 'system' ? null : mode);
    apply(mode);
  },

  toggle() {
    this.set(this.isDark ? 'light' : 'dark');
  },

  toggleSaver() {
    this.saver = !this.saver;
    write('k-saver', this.saver ? '1' : null);
    if (this.saver) root.dataset.saver = '1'; else delete root.dataset.saver;
  },
};

media.addEventListener('change', () => { if (theme.mode === 'system') apply('system'); });
