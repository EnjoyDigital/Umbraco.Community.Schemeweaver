import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  build: {
    lib: {
      entry: resolve(__dirname, 'index.ts'),
      name: 'SchemeWeaverAI',
      fileName: () => 'index.js',
      formats: ['es'],
    },
    rollupOptions: {
      external: (id) => {
        if (id.startsWith('@umbraco-cms/backoffice') || id.startsWith('lit')) {
          return true;
        }
        return false;
      },
      output: {
        format: 'es',
        chunkFileNames: '[name]-[hash].js',
      },
    },
    outDir: '../../wwwroot-ai',
    // Don't empty the dir so umbraco-package.json (committed to wwwroot-ai/) is preserved.
    emptyOutDir: false,
  },
});
