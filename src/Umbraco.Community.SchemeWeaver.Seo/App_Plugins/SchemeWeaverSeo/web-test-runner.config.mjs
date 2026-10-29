import { esbuildPlugin } from '@web/dev-server-esbuild';
import { importMapsPlugin } from '@web/dev-server-import-maps';
import { playwrightLauncher } from '@web/test-runner-playwright';

// Served from the repo's src/ folder so the shared core modules (aliased as
// src/core-bridge/) and the core's import-map mocks are reachable.
const CORE = '/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/src';
const MOCKS = `${CORE}/__mocks__`;

export const importMap = {
  imports: {
    '@umbraco-cms/backoffice/external/lit': `${MOCKS}/lit.js`,
    '@umbraco-cms/backoffice/lit-element': `${MOCKS}/lit-element.js`,
    '@umbraco-cms/backoffice/modal': `${MOCKS}/modal.js`,
    '@umbraco-cms/backoffice/entity-action': `${MOCKS}/entity-action.js`,
    '@umbraco-cms/backoffice/workspace': `${MOCKS}/workspace.js`,
    '@umbraco-cms/backoffice/notification': `${MOCKS}/notification.js`,
    '@umbraco-cms/backoffice/style': `${MOCKS}/style.js`,
    '@umbraco-cms/backoffice/controller-api': `${MOCKS}/controller.js`,
    '@umbraco-cms/backoffice/class-api': `${MOCKS}/class-api.js`,
    '@umbraco-cms/backoffice/context-api': `${MOCKS}/context-api.js`,
    '@umbraco-cms/backoffice/observable-api': `${MOCKS}/observable-api.js`,
    '@umbraco-cms/backoffice/resources': `${MOCKS}/resources.js`,
    '@umbraco-cms/backoffice/extension-registry': `${MOCKS}/extension-registry.js`,
    '@umbraco-cms/backoffice/document': `${MOCKS}/document.js`,
    '@umbraco-cms/backoffice/media': '/Umbraco.Community.SchemeWeaver.Seo/App_Plugins/SchemeWeaverSeo/src/__mocks__/media.js',
    '@umbraco-cms/backoffice/auth': '/Umbraco.Community.SchemeWeaver.Seo/App_Plugins/SchemeWeaverSeo/src/__mocks__/auth.js',
  },
};

/**
 * The MSW worker script is served below the server root (the repo's src/ folder), so the
 * browser would scope it to its own directory; this header lets it claim '/' and intercept
 * the management API calls.
 */
export const serviceWorkerAllowedMiddleware = (ctx, next) => {
  if (ctx.path.endsWith('/mockServiceWorker.js')) {
    ctx.set('Service-Worker-Allowed', '/');
  }
  return next();
};

export default {
  rootDir: '../../..',
  middleware: [serviceWorkerAllowedMiddleware],
  files: 'src/**/*.test.ts',
  // msw 3's @mswjs/interceptors picks its browser build via the `browser` export condition;
  // without it the Node build (node:http et al.) is served to Chromium and every msw-importing
  // test file fails with "Failed to fetch dynamically imported module".
  nodeResolve: { browser: true, exportConditions: ['browser', 'development'] },
  testsFinishTimeout: 300000,
  browsers: [playwrightLauncher({ product: 'chromium' })],
  plugins: [
    importMapsPlugin({ inject: { importMap } }),
    esbuildPlugin({ ts: true, tsconfig: './tsconfig.json' }),
  ],
};
