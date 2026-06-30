// One-time context fetcher for the eval harness.
//
// Context (content-type properties incl. valueSchema, block element structure, ranked
// schema properties, and the heuristic baseline) is STABLE across prompt iterations, so
// we fetch it once via the proven MCP client (client-credentials auth) and cache to
// eval/cache/context.json. The prompt-tuning loop then only varies the prompt + re-calls
// Anthropic — no Umbraco round-trips per iteration.
//
// Usage:  node eval/fetch-context.mjs
// Requires: TestHost running on :44308 and the MCP API user provisioned.

import { spawnSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { SAMPLE_ALIASES } from './sample.mjs';
import { loadAllGold } from './gold.mjs';

const REPO = process.cwd();
const MCP_DIR = join(REPO, 'src/Umbraco.Community.SchemeWeaver.Mcp');
const CACHE_DIR = join(REPO, 'eval/cache');

const BLOCK_EDITORS = new Set(['Umbraco.BlockList', 'Umbraco.BlockGrid']);

/**
 * Call an MCP tool via its CLI and return the unwrapped result.
 *
 * The MCP CLI prints valid JSON to stdout but crashes on process teardown on Windows
 * (a libuv UV_HANDLE_CLOSING assertion -> non-zero exit), so we use spawnSync and parse
 * stdout regardless of exit code rather than trusting the exit status.
 */
function mcp(tool, args) {
  const res = spawnSync(
    'node',
    ['--env-file=.env', 'dist/index.js', '--call', tool, '--call-args', JSON.stringify(args)],
    { cwd: MCP_DIR, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 },
  );
  const out = res.stdout || '';
  const start = out.search(/[[{]/);
  if (start < 0) throw new Error(`no JSON in output: ${(res.stderr || out).slice(0, 200)}`);
  const parsed = JSON.parse(out.slice(start));
  return parsed.items ?? parsed;
}

async function main() {
  mkdirSync(CACHE_DIR, { recursive: true });
  const gold = loadAllGold();
  const context = {};

  for (const alias of SAMPLE_ALIASES) {
    const g = gold.get(alias.toLowerCase());
    if (!g) {
      console.warn(`! no gold for ${alias} — skipping`);
      continue;
    }
    process.stdout.write(`• ${alias} -> ${g.schemaType} ... `);
    try {
      const ctProps = mcp('get-content-type-properties', { alias });

      // Fetch block element structure for any Block List / Block Grid property.
      const blocks = {};
      for (const p of ctProps) {
        if (BLOCK_EDITORS.has(p.editorAlias)) {
          try {
            blocks[p.alias] = mcp('get-block-element-types', {
              contentTypeAlias: alias,
              propertyAlias: p.alias,
            });
          } catch (e) {
            blocks[p.alias] = { error: String(e.message || e).slice(0, 200) };
          }
        }
      }

      const schemaProps = mcp('get-schema-type-properties', { name: g.schemaType, ranked: true });
      const heuristic = mcp('suggest-property-mappings', {
        contentTypeAlias: alias,
        schemaTypeName: g.schemaType,
      });

      context[alias] = {
        alias,
        schemaType: g.schemaType,
        contentProperties: ctProps,
        blockElementTypes: blocks,
        rankedSchemaProperties: schemaProps,
        heuristicBaseline: heuristic,
      };
      console.log(`ok (${ctProps.length} props, ${Object.keys(blocks).length} block props, ${schemaProps.length} schema props)`);
    } catch (e) {
      console.log(`FAILED: ${String(e.message || e).slice(0, 200)}`);
    }
  }

  const path = join(CACHE_DIR, 'context.json');
  writeFileSync(path, JSON.stringify(context, null, 2));
  console.log(`\nWrote ${Object.keys(context).length} types -> ${path}`);
}

main();
