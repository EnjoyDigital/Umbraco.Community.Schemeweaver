import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';

// Light-weight module read from every worker and from playwright.config.ts.
// The heavy lifting (patch check, src mirroring) happens in
// `global-setup.ts`, which Playwright runs exactly once per test run
// before any worker starts, so this module can stay side-effect-free.

const MIRROR_SUBPATH = 'examples/schemeweaver-src';

function resolveUmbracoClientPath(): string {
  const raw = process.env.UMBRACO_CLIENT_PATH;
  if (!raw) {
    throw new Error(
      '[mocked-backoffice] UMBRACO_CLIENT_PATH is not set. Point it at your local ' +
        'Umbraco-CMS/src/Umbraco.Web.UI.Client checkout, e.g.\n' +
        '  export UMBRACO_CLIENT_PATH=/c/projects/Umbraco-CMS/src/Umbraco.Web.UI.Client',
    );
  }
  const resolved = resolve(raw);
  if (!existsSync(join(resolved, 'package.json'))) {
    throw new Error(
      `[mocked-backoffice] UMBRACO_CLIENT_PATH "${resolved}" does not look like an ` +
        'Umbraco.Web.UI.Client checkout (no package.json).',
    );
  }
  return resolved;
}

export const UMBRACO_CLIENT_PATH = resolveUmbracoClientPath();
export const VITE_EXAMPLE_PATH = MIRROR_SUBPATH;
export const DEV_SERVER_PORT = 5174;
export const BASE_URL = `http://localhost:${DEV_SERVER_PORT}`;
