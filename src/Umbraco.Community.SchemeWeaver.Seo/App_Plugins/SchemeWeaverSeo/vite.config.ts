import { defineConfig } from 'vite';
import { resolve } from 'path';

// The SEO bundle compiles a handful of the core plugin's pure TypeScript modules (DTO types,
// the authenticated fetch helper, the core context token) straight from the core's source tree.
// Only `src/core-bridge/` may import them — see that folder and scripts/check-core-bridge.mjs.

export default defineConfig({
  build: {
    lib: {
      entry: resolve(__dirname, 'src/index.ts'),
      name: 'SchemeWeaverSeo',
      fileName: () => 'index.js',
      formats: ['es'],
    },
    rollupOptions: {
      external: (id) => id.startsWith('@umbraco-cms/backoffice') || id.startsWith('lit'),
      output: {
        format: 'es',
        chunkFileNames: '[name]-[hash].js',
      },
    },
    outDir: '../../wwwroot/dist',
    emptyOutDir: true,
  },
  resolve: {
    alias: {
      '@umbraco-cms/backoffice/lit': '@umbraco-cms/backoffice/external/lit',
    },
  },
  server: {
    fs: { allow: [resolve(__dirname, '../../..')] },
  },
});
