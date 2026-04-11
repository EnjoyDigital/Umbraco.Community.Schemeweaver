import { cpSync, existsSync, readFileSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';

/**
 * Playwright globalSetup — runs exactly once per test run before any
 * worker starts. Handles everything that must happen in a single
 * critical section: verifying the Umbraco-CMS patch is applied, and
 * mirroring SchemeWeaver's `src/` into the client's `examples/`
 * directory so Vite's `fs.allow` lets it through.
 *
 * Doing the mirror here (rather than in `fixtures/env.ts` which is
 * loaded by every worker) avoids the race where two workers both race
 * to rm/cp the same directory and one sees the other's half-finished
 * state.
 */

const PINNED_UMBRACO_VERSION = '17.2.2';
const MIRROR_SUBPATH = 'examples/schemeweaver-src';

// tests/mocked-backoffice/fixtures → App_Plugins/SchemeWeaver/src
const SCHEMEWEAVER_SRC = resolve(__dirname, '..', '..', '..', 'src');

function fail(message: string): never {
  throw new Error(`[mocked-backoffice] ${message}`);
}

function resolveUmbracoClientPath(): string {
  const raw = process.env.UMBRACO_CLIENT_PATH;
  if (!raw) {
    fail(
      'UMBRACO_CLIENT_PATH is not set. Point it at your local ' +
        'Umbraco-CMS/src/Umbraco.Web.UI.Client checkout, e.g.\n' +
        '  export UMBRACO_CLIENT_PATH=/c/projects/Umbraco-CMS/src/Umbraco.Web.UI.Client',
    );
  }
  const resolved = resolve(raw);
  if (!existsSync(join(resolved, 'package.json'))) {
    fail(`UMBRACO_CLIENT_PATH "${resolved}" does not look like an Umbraco.Web.UI.Client checkout (no package.json).`);
  }
  return resolved;
}

function assertPatchApplied(umbracoClientPath: string): void {
  const mocksIndex = join(umbracoClientPath, 'src', 'mocks', 'index.ts');
  if (!existsSync(mocksIndex)) {
    fail(`Could not find ${mocksIndex} — is UMBRACO_CLIENT_PATH correct?`);
  }
  const content = readFileSync(mocksIndex, 'utf-8');
  if (!content.includes('addMockHandlers')) {
    fail(
      'The Umbraco-CMS clone at UMBRACO_CLIENT_PATH is missing the `addMockHandlers` hook.\n' +
        'Apply the patch with:\n' +
        `  git -C "${umbracoClientPath}" apply "${resolve(__dirname, '..', 'patches', 'umbraco-cms-v17.2.2-addmockhandlers.patch')}"\n` +
        'or hand-merge the same change into src/mocks/index.ts.',
    );
  }
}

function warnOnVersionDrift(umbracoClientPath: string): void {
  try {
    const pkg = JSON.parse(readFileSync(join(umbracoClientPath, 'package.json'), 'utf-8'));
    if (pkg?.version && pkg.version !== PINNED_UMBRACO_VERSION) {
      // eslint-disable-next-line no-console
      console.warn(
        `[mocked-backoffice] Umbraco-CMS version ${pkg.version} differs from the pinned ${PINNED_UMBRACO_VERSION}. ` +
          'The patch may still apply but tests were authored against the pinned version.',
      );
    }
  } catch {
    // swallow — a missing/invalid package.json is already surfaced elsewhere.
  }
}

function mirrorSchemeWeaverSrc(umbracoClientPath: string): void {
  const mirrorPath = join(umbracoClientPath, MIRROR_SUBPATH);

  if (!existsSync(SCHEMEWEAVER_SRC)) {
    fail(`SchemeWeaver src directory not found at ${SCHEMEWEAVER_SRC}`);
  }

  // Refuse to delete anything that isn't ours — guard against stray
  // edits that might have an unrelated folder at the same path.
  if (existsSync(mirrorPath)) {
    const marker = join(mirrorPath, 'manifests.ts');
    if (!existsSync(marker)) {
      fail(
        `Refusing to delete ${mirrorPath} — it exists but does not look like a ` +
          'SchemeWeaver mirror (no manifests.ts inside). Move it out of the way manually.',
      );
    }
    rmSync(mirrorPath, { recursive: true, force: true });
  }

  cpSync(SCHEMEWEAVER_SRC, mirrorPath, { recursive: true });
}

export default function globalSetup(): void {
  const umbracoClientPath = resolveUmbracoClientPath();
  assertPatchApplied(umbracoClientPath);
  warnOnVersionDrift(umbracoClientPath);
  mirrorSchemeWeaverSrc(umbracoClientPath);
}
