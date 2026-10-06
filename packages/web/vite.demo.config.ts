import { defineConfig, type Plugin } from 'vite';
import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';

const fontsDir = resolve(import.meta.dirname, '../../design/fonts');

/** Serves design/fonts at /fonts/* in dev and emits them into the demo build. */
function slateFonts(): Plugin {
  return {
    name: 'slate-fonts',
    configureServer(server) {
      server.middlewares.use('/fonts', (req, res, next) => {
        const file = decodeURIComponent((req.url ?? '').replace(/^\//, '').split('?')[0]);
        if (!/^[\w.-]+\.ttf$/.test(file)) return next();
        try {
          res.setHeader('Content-Type', 'font/ttf');
          res.end(readFileSync(resolve(fontsDir, file)));
        } catch {
          next();
        }
      });
    },
    generateBundle() {
      for (const file of readdirSync(fontsDir).filter((f) => f.endsWith('.ttf'))) {
        this.emitFile({ type: 'asset', fileName: `fonts/${file}`, source: readFileSync(resolve(fontsDir, file)) });
      }
    },
  };
}

export default defineConfig({
  root: 'demo',
  base: './',
  plugins: [slateFonts()],
  build: { outDir: '../dist-demo', emptyOutDir: true },
});
