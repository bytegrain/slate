import { defineConfig } from 'vitest/config';

// Library build: dist/slate.js (ESM). Lit stays external so apps share one copy.
export default defineConfig({
  build: {
    lib: {
      entry: 'src/index.ts',
      formats: ['es'],
      fileName: () => 'slate.js',
    },
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      external: [/^lit($|\/)/, /^@lit\//],
    },
  },
  test: {
    environment: 'happy-dom',
    include: ['test/**/*.test.ts'],
    setupFiles: ['test/setup.ts'],
    // Process CSS so ?raw / ?inline imports carry real content (tokens are read from tokens.css).
    css: { include: [/.+/] },
  },
});
