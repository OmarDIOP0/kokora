// Build des scripts (esbuild) + copie des polices et icônes.
// Usage : node Assets/build.mjs [--watch]
import * as esbuild from 'esbuild';
import { cp, mkdir, readdir } from 'node:fs/promises';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..');
const watch = process.argv.includes('--watch');

// Polices auto-hébergées : sous-ensemble latin (couvre le français), graisses utilisées uniquement.
const fonts = [
  ['@fontsource/barlow-condensed', 'barlow-condensed-latin-500-normal.woff2'],
  ['@fontsource/barlow-condensed', 'barlow-condensed-latin-600-normal.woff2'],
  ['@fontsource/barlow-condensed', 'barlow-condensed-latin-700-normal.woff2'],
  ['@fontsource/barlow', 'barlow-latin-400-normal.woff2'],
  ['@fontsource/barlow', 'barlow-latin-500-normal.woff2'],
  ['@fontsource/barlow', 'barlow-latin-600-normal.woff2'],
];
await mkdir(path.join(root, 'wwwroot/fonts'), { recursive: true });
for (const [pkg, file] of fonts)
  await cp(path.join(root, 'node_modules', pkg, 'files', file), path.join(root, 'wwwroot/fonts', file));

// Icônes Lucide (SVG) lues côté serveur par le tag helper <icon>.
const iconsSrc = path.join(root, 'node_modules/lucide-static/icons');
const iconsDst = path.join(root, 'Icons');
await mkdir(iconsDst, { recursive: true });
for (const f of await readdir(iconsSrc)) if (f.endsWith('.svg')) await cp(path.join(iconsSrc, f), path.join(iconsDst, f));

/** @type {esbuild.BuildOptions} */
const options = {
  entryPoints: {
    core: 'Assets/js/core.js',
    design: 'Assets/js/design.js',
    admin: 'Assets/js/admin.js',
    charts: 'Assets/js/charts.js',
    editor: 'Assets/js/editor.js',
    gallery: 'Assets/js/gallery.js',
  },
  absWorkingDir: root,
  outdir: 'wwwroot/dist',
  bundle: true,
  splitting: true,
  format: 'esm',
  target: ['es2020', 'chrome87', 'safari15', 'firefox90'],
  minify: !watch,
  sourcemap: watch ? 'inline' : false,
  chunkNames: 'chunks/[name]-[hash]',
  loader: { '.woff2': 'file' },
  logLevel: 'info',
  legalComments: 'none',
};

if (watch) {
  const ctx = await esbuild.context(options);
  await ctx.watch();
} else {
  const result = await esbuild.build({ ...options, metafile: true });
  const sizes = Object.entries(result.metafile.outputs)
    .map(([file, o]) => `${(o.bytes / 1024).toFixed(1).padStart(7)} Ko  ${file}`);
  console.log(sizes.join('\n'));
}
