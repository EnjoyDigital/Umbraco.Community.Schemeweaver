// Cache-busts the SEO bundle entry URL after a Vite build (same reasoning as the core's
// scripts/stamp-cachebust.mjs, issue #21): the entry URL is fixed but its chunks are
// content-hashed and replaced on every build, so a stale cached index.js would 404.
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
// scripts/ -> App_Plugins/SchemeWeaverSeo -> App_Plugins -> <project> -> wwwroot
const distIndex = resolve(here, '../../../wwwroot/dist/index.js');
const packageJsonPath = resolve(here, '../../../wwwroot/umbraco-package.json');

const ENTRY_PATH = '/App_Plugins/SchemeWeaverSeo/dist/index.js';

const hash = createHash('sha256').update(readFileSync(distIndex)).digest('hex').slice(0, 8);

const pkg = JSON.parse(readFileSync(packageJsonPath, 'utf8'));
const bundle = pkg.extensions?.find((e) => e.type === 'bundle');
if (!bundle) {
  throw new Error(`No bundle extension found in ${packageJsonPath} — cannot stamp cache-bust token.`);
}

bundle.js = `${ENTRY_PATH}?v=${hash}`;

writeFileSync(packageJsonPath, `${JSON.stringify(pkg, null, 2)}\n`);
console.log(`stamp-cachebust: set bundle js -> ${bundle.js}`);
