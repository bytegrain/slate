// Builds dist/slate.css (fonts + every stylesheet from src/styles/index.css, flattened in order),
// dist/tokens.css, and copies the font files + licences to dist/fonts.
import { copyFileSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const styles = join(root, 'src/styles');
const dist = join(root, 'dist');
const fontsSrc = resolve(root, '../../design/fonts');

mkdirSync(join(dist, 'fonts'), { recursive: true });

const imports = [...readFileSync(join(styles, 'index.css'), 'utf8').matchAll(/@import\s+"\.\/([^"]+)";/g)].map((m) => m[1]);
if (imports.length === 0) throw new Error('No @import entries found in src/styles/index.css');

const pkg = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
const parts = [
  `/*! @slate/web ${pkg.version} — Alloy design system. Fonts: SIL OFL 1.1 (see fonts/). */`,
  readFileSync(join(styles, 'fonts.css'), 'utf8'),
  ...imports.map((file) => `/* ---- ${file} ---- */\n${readFileSync(join(styles, file), 'utf8')}`),
];
writeFileSync(join(dist, 'slate.css'), parts.join('\n'));
copyFileSync(join(styles, 'generated/tokens.css'), join(dist, 'tokens.css'));

let fonts = 0;
for (const file of readdirSync(fontsSrc)) {
  if (/\.(ttf|txt)$/i.test(file)) {
    copyFileSync(join(fontsSrc, file), join(dist, 'fonts', file));
    fonts++;
  }
}
console.log(`slate.css: ${imports.length} stylesheets; ${fonts} font/licence files copied.`);
