import base, { importMap, serviceWorkerAllowedMiddleware } from './web-test-runner.config.mjs';
import { esbuildPlugin } from '@web/dev-server-esbuild';
import { importMapsPlugin } from '@web/dev-server-import-maps';

// Same suite with the MSW service worker available (used by the data-source tests).
export default {
  ...base,
  nodeResolve: { browser: true, exportConditions: ['browser', 'development'] },
  middleware: [serviceWorkerAllowedMiddleware],
  plugins: [
    importMapsPlugin({ inject: { importMap } }),
    esbuildPlugin({ ts: true, tsconfig: './tsconfig.json' }),
  ],
  testRunnerHtml: (testFramework) => `
    <html>
      <body>
        <script src="/Umbraco.Community.SchemeWeaver.Seo/App_Plugins/SchemeWeaverSeo/node_modules/msw/lib/iife/index.js"></script>
        <script type="module" src="${testFramework}"></script>
      </body>
    </html>`,
};
