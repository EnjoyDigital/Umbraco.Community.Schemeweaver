// Cache-busts the backoffice bundle entry URL after a Vite build.
//
// `umbraco-package.json` registers the bundle at a fixed, non-fingerprinted URL
// (/App_Plugins/SchemeWeaver/dist/index.js). Browsers cache that entry
// indefinitely, but the chunks it lazily imports ARE content-hashed and get
// deleted on every rebuild (vite emptyOutDir). After a package upgrade a stale
// cached index.js therefore tries to import a chunk hash that no longer exists,
// 404s, and the whole SchemeWeaver UI fails to load (see issue #21).
//
// Fix: append ?v=<hash-of-index.js> to the entry URL. The token derives from the
// bundle content, so it changes exactly when the bundle changes — every upgrade
// busts the cache automatically and the bug can never silently recur. The chunks
// stay immutably cacheable by their own content hash.
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
// scripts/ -> App_Plugins/SchemeWeaver -> App_Plugins -> <project> -> wwwroot
const distIndex = resolve(here, '../../../wwwroot/dist/index.js');
const packageJsonPath = resolve(here, '../../../wwwroot/umbraco-package.json');

const ENTRY_PATH = '/App_Plugins/SchemeWeaver/dist/index.js';

const hash = createHash('sha256').update(readFileSync(distIndex)).digest('hex').slice(0, 8);

const pkg = JSON.parse(readFileSync(packageJsonPath, 'utf8'));
const bundle = pkg.extensions?.find((e) => e.type === 'bundle');
if (!bundle) {
  throw new Error(`No bundle extension found in ${packageJsonPath} — cannot stamp cache-bust token.`);
}

bundle.js = `${ENTRY_PATH}?v=${hash}`;

writeFileSync(packageJsonPath, `${JSON.stringify(pkg, null, 2)}\n`);
console.log(`stamp-cachebust: set bundle js -> ${bundle.js}`);
