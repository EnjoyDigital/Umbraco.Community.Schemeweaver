// Guard: only src/core-bridge/ may import the core plugin's source (by relative path), and nothing may value-import the core context class or its element modules, or the
// two bundles would double-register custom elements / contexts. Runs before the test suite.
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../src');
const violations = [];

function walk(dir) {
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) {
      walk(path);
      continue;
    }
    if (!path.endsWith('.ts')) continue;
    const rel = relative(root, path);
    const text = readFileSync(path, 'utf8');
    const inBridge = rel.startsWith('core-bridge/') || rel.startsWith('core-bridge\\');
    for (const line of text.split('\n')) {
      if (!inBridge && line.includes('Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/')) {
        violations.push(`${rel}: imports the core plugin's source outside core-bridge/`);
      }
      if (inBridge && /^\s*(import|export)\s+(?!type\b)[^;]*\/(components\/|context\/schemeweaver\.context\.js)/.test(line)) {
        violations.push(`${rel}: value-imports a core element/context module (${line.trim()})`);
      }
    }
  }
}

walk(root);
if (violations.length) {
  console.error('core-bridge guard failed:\n  ' + violations.join('\n  '));
  process.exit(1);
}
console.log('core-bridge guard: ok');
