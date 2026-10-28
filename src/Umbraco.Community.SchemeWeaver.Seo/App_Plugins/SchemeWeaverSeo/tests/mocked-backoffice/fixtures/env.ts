import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';

// Light-weight module read from every worker and from playwright.config.ts. The heavy
// lifting (patch check, composite mirror) happens in global-setup.ts.

const MIRROR_SUBPATH = 'examples/schemeweaver-seo';

function resolveUmbracoClientPath(): string {
  const raw = process.env.UMBRACO_CLIENT_PATH;
  if (!raw) {
    throw new Error(
      '[mocked-backoffice] UMBRACO_CLIENT_PATH is not set. Point it at your local ' +
        'Umbraco-CMS/src/Umbraco.Web.UI.Client checkout (pinned to v17.2.2, patched — see the core harness README).',
    );
  }
  const resolved = resolve(raw);
  if (!existsSync(join(resolved, 'package.json'))) {
    throw new Error(`[mocked-backoffice] UMBRACO_CLIENT_PATH "${resolved}" does not look like an Umbraco.Web.UI.Client checkout.`);
  }
  return resolved;
}

export const UMBRACO_CLIENT_PATH = resolveUmbracoClientPath();
export const VITE_EXAMPLE_PATH = MIRROR_SUBPATH;
export const DEV_SERVER_PORT = 5175;
export const BASE_URL = `http://localhost:${DEV_SERVER_PORT}`;
