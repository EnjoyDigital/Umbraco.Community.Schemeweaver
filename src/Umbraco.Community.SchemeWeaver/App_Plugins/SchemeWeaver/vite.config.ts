import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  build: {
    lib: {
      entry: resolve(__dirname, 'src/index.ts'),
      name: 'SchemeWeaver',
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
});
