// Graphiques (Chart.js), chargés seulement sur les pages qui en contiennent.
// Import sélectif des modules Chart.js pour garder un bundle léger.
import { Chart, BarController, BarElement, CategoryScale, LinearScale, Tooltip, Legend } from 'chart.js';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip, Legend);

const css = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

function goalsChart(canvas) {
  const values = JSON.parse(canvas.dataset.values || '[]');
  return new Chart(canvas, {
    type: 'bar',
    data: {
      labels: values.map((v) => v.label),
      datasets: [
        { label: 'Marqués', data: values.map((v) => v.gf), backgroundColor: css('--brand'), borderRadius: 3, maxBarThickness: 18 },
        { label: 'Encaissés', data: values.map((v) => v.ga), backgroundColor: css('--line-strong'), borderRadius: 3, maxBarThickness: 18 },
      ],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: { duration: 200 },
      plugins: {
        legend: { position: 'bottom', labels: { color: css('--text-2'), boxWidth: 10, boxHeight: 10, font: { family: 'Barlow', size: 12 } } },
        tooltip: { displayColors: false },
      },
      scales: {
        x: { grid: { display: false }, ticks: { color: css('--text-3'), font: { family: 'Barlow', size: 11 } } },
        y: { beginAtZero: true, ticks: { stepSize: 1, precision: 0, color: css('--text-3') }, grid: { color: css('--line') }, border: { display: false } },
      },
    },
  });
}

// Un graphique n'est créé qu'une fois visible : dans un onglet masqué, sa taille serait nulle.
const charts = new Map();
const observer = new IntersectionObserver((entries) => {
  for (const entry of entries) {
    if (entry.isIntersecting && !charts.has(entry.target)) charts.set(entry.target, goalsChart(entry.target));
  }
});

function start() {
  document.querySelectorAll('canvas[data-chart="goals"]').forEach((c) => observer.observe(c));
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();

// Couleurs recalculées au changement de thème clair/sombre.
new MutationObserver(() => {
  for (const [canvas, chart] of charts) {
    chart.destroy();
    charts.set(canvas, goalsChart(canvas));
  }
}).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
