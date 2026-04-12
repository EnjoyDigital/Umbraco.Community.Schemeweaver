import { esbuildPlugin } from '@web/dev-server-esbuild';
import { importMapsPlugin } from '@web/dev-server-import-maps';
import { playwrightLauncher } from '@web/test-runner-playwright';

export default {
  rootDir: '.',
  files: ['./src/**/*.test.ts'],
  nodeResolve: { exportConditions: ['development'], preferBuiltins: false, browser: true },
  browsers: [playwrightLauncher({ product: 'chromium' })],
  plugins: [
    importMapsPlugin({
      inject: {
        importMap: {
          imports: {
            '@umbraco-cms/backoffice/external/lit': '/src/__mocks__/lit.js',
            '@umbraco-cms/backoffice/lit-element': '/src/__mocks__/lit-element.js',
            '@umbraco-cms/backoffice/modal': '/src/__mocks__/modal.js',
            '@umbraco-cms/backoffice/entity-action': '/src/__mocks__/entity-action.js',
            '@umbraco-cms/backoffice/workspace': '/src/__mocks__/workspace.js',
            '@umbraco-cms/backoffice/notification': '/src/__mocks__/notification.js',
            '@umbraco-cms/backoffice/controller-api': '/src/__mocks__/controller.js',
            '@umbraco-cms/backoffice/class-api': '/src/__mocks__/class-api.js',
            '@umbraco-cms/backoffice/context-api': '/src/__mocks__/context-api.js',
            '@umbraco-cms/backoffice/observable-api': '/src/__mocks__/observable-api.js',
            '@umbraco-cms/backoffice/resources': '/src/__mocks__/resources.js',
            '@umbraco-cms/backoffice/extension-registry': '/src/__mocks__/extension-registry.js',
            '@umbraco-cms/backoffice/document': '/src/__mocks__/document.js',
          },
        },
      },
    }),
    esbuildPlugin({ ts: true, tsconfig: './tsconfig.json', target: 'auto', json: true }),
  ],
  testRunnerHtml: (testFramework) =>
    `<html>
      <head>
        <script src="/node_modules/msw/lib/iife/index.js"></script>
      </head>
      <body>
        <script type="module" src="${testFramework}"></script>
      </body>
    </html>`,
};
