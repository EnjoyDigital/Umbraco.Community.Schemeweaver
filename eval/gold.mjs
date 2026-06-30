// Gold-standard parser for SchemeWeaver eval harness.
//
// Parses the curated uSync mapping .config files in the TestHost into structured
// expected-mapping sets we can score AI / heuristic suggestions against.
//
// The .config XML is small and regular; we parse per <PropertyMapping> block with
// targeted regexes (ResolverConfig is CDATA). No XML dependency needed.

import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

export const GOLD_DIR = join(
  process.cwd(),
  'src/Umbraco.Community.SchemeWeaver.TestHost/uSync/v18/SchemeWeaverMappings',
);

const BUILTINS = new Set(['__name', '__url', '__createDate', '__updateDate']);

// "Rich" = the source types the heuristic struggles with and the AI should win on.
// Split into self-contained (derivable from the content type alone) vs cross-node
// (needs knowledge of other content types in the tree).
export const SELF_CONTAINED_RICH = new Set(['complexType', 'blockContent']);
export const CROSS_NODE_RICH = new Set(['ancestor', 'parent', 'sibling']);

function tag(xml, name) {
  const m = xml.match(new RegExp(`<${name}>([\\s\\S]*?)</${name}>`));
  return m ? m[1].trim() : null;
}

function cdata(xml, name) {
  const m = xml.match(new RegExp(`<${name}><!\\[CDATA\\[([\\s\\S]*?)\\]\\]></${name}>`));
  if (m) return m[1].trim();
  // ResolverConfig is sometimes written without CDATA
  return tag(xml, name);
}

/** Parse a single .config file into a structured gold mapping. */
export function parseGoldFile(path) {
  const xml = readFileSync(path, 'utf8');
  const alias = (xml.match(/<ContentTypeAlias>([^<]+)<\/ContentTypeAlias>/) || [])[1];
  const schemaType = (xml.match(/<SchemaTypeName>([^<]+)<\/SchemaTypeName>/) || [])[1];

  const blocks = [...xml.matchAll(/<PropertyMapping>([\s\S]*?)<\/PropertyMapping>/g)].map((m) => m[1]);
  const mappings = blocks.map((b) => {
    const schemaProp = tag(b, 'SchemaPropertyName');
    const sourceType = tag(b, 'SourceType') || 'property';
    const contentProp = tag(b, 'ContentTypePropertyAlias');
    const nestedType = tag(b, 'NestedSchemaTypeName');
    const sourceContentType = tag(b, 'SourceContentTypeAlias');
    const resolverConfig = cdata(b, 'ResolverConfig');
    const targetPieceKey = tag(b, 'TargetPieceKey');
    const staticValue = tag(b, 'StaticValue');
    return {
      schemaProp,
      sourceType,
      contentProp,
      nestedType,
      sourceContentType,
      resolverConfig: resolverConfig ? safeJson(resolverConfig) : null,
      targetPieceKey,
      staticValue,
      isBuiltIn: contentProp != null && BUILTINS.has(contentProp),
      isSelfContainedRich: SELF_CONTAINED_RICH.has(sourceType),
      isCrossNodeRich: CROSS_NODE_RICH.has(sourceType),
    };
  });

  return { alias, schemaType, mappings };
}

function safeJson(s) {
  try {
    return JSON.parse(s);
  } catch {
    return { _raw: s };
  }
}

/** Load every gold mapping keyed by content type alias (lowercased). */
export function loadAllGold() {
  const out = new Map();
  for (const f of readdirSync(GOLD_DIR).filter((f) => f.endsWith('.config'))) {
    const g = parseGoldFile(join(GOLD_DIR, f));
    if (g.alias) out.set(g.alias.toLowerCase(), g);
  }
  return out;
}

/** Summary counts of rich-mapping coverage across the whole gold set. */
export function goldRichSummary() {
  const all = loadAllGold();
  let selfRich = 0;
  let crossRich = 0;
  const selfRichTypes = [];
  const crossRichTypes = [];
  for (const [, g] of all) {
    const s = g.mappings.filter((m) => m.isSelfContainedRich).length;
    const c = g.mappings.filter((m) => m.isCrossNodeRich).length;
    if (s) {
      selfRich += s;
      selfRichTypes.push(`${g.alias}(${s})`);
    }
    if (c) {
      crossRich += c;
      crossRichTypes.push(`${g.alias}(${c})`);
    }
  }
  return { total: all.size, selfRich, crossRich, selfRichTypes, crossRichTypes };
}

// CLI: `node eval/gold.mjs` prints the rich-mapping summary and a sample dump.
if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const s = goldRichSummary();
  console.log('Gold rich-mapping summary:');
  console.log(`  total content types mapped : ${s.total}`);
  console.log(`  self-contained rich maps   : ${s.selfRich}  [${s.selfRichTypes.join(', ')}]`);
  console.log(`  cross-node rich maps       : ${s.crossRich}  [${s.crossRichTypes.join(', ')}]`);
  const arg = process.argv[2];
  if (arg) {
    const g = loadAllGold().get(arg.toLowerCase());
    console.log(`\nGold for ${arg}:`);
    console.log(JSON.stringify(g, null, 2));
  }
}
