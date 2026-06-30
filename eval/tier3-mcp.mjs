// Tier-3: MCP-driven mapping eval.
//
// Simulates a power user's Claude (their own subscription) driving the SchemeWeaver MCP tools to
// map a content type, then scores the mapping it SAVES against gold with the same scorer as
// Tier-1/2. This proves the MCP path reaches the AI satellite's quality without the satellite —
// the model is the intelligence, the tools are the MCP surface, the heuristic is irrelevant.
//
// Faithful to the MCP flow: the model is given the same tools the MCP server exposes (here wired
// straight to the live management endpoints the server wraps) plus the satellite playbook, and it
// runs the inspect -> choose type -> rank -> baseline -> save -> preview -> fix loop itself.
//
// Non-destructive: snapshots each mapping, lets the model remap from a clean slate, scores its
// save payload, then restores the original gold mapping. Requires the TestHost on :44308.
//
// Usage: node eval/tier3-mcp.mjs
import { loadAllGold } from './gold.mjs';
import { SAMPLE } from './sample.mjs';
import { scoreOne, aggregate } from './score.mjs';
import { getApiKey, MODEL } from './anthropic.mjs';
import { SYSTEM } from './prompt.mjs';
import { api } from './live-client.mjs';

const pct = (n) => `${Math.round(n * 100)}%`;
const MAX_TURNS = 16;
// `--limit N` runs only the first N rich types (smoke-test the harness before a full, paid run).
const limitArg = process.argv.indexOf('--limit');
const LIMIT = limitArg > 0 ? Number(process.argv[limitArg + 1]) : Infinity;
const RICH = SAMPLE.filter((s) => s.tag === 'rich').slice(0, LIMIT);

const SYSTEM_TIER3 =
  SYSTEM +
  '\n\n--- TOOL PROTOCOL (this OVERRIDES the JSON-array output format described above) ---\n' +
  'You have MCP tools to inspect and persist a mapping. Map the requested content type by:\n' +
  '1. get_content_type_properties; for Block List/Grid properties also call get_block_element_types.\n' +
  '2. Choose the MOST SPECIFIC fitting Schema.org type via search_schema_types.\n' +
  '3. get_schema_type_properties (ranked=true) and prioritise the Google required/recommended props.\n' +
  '4. suggest_property_mappings for the heuristic baseline, then IMPROVE it semantically.\n' +
  '5. Call save_schema_mapping with the best mapping, using the source types above — reach for\n' +
  '   complexType / blockContent (routes | nestedMappings | stringList) where the structure warrants it.\n' +
  '6. Call preview_json_ld; if it reports issues, fix the mapping and save_schema_mapping again.\n' +
  'Do NOT emit a JSON array — every mapping is expressed through the save_schema_mapping tool.\n' +
  'Finish once the mapping is complete and the preview is clean.';

// Tool schemas advertised to the model. Kept close to the real MCP tool surface.
function toolDefs() {
  return [
    { name: 'get_content_type_properties', description: 'List a content type\'s properties (alias, editorAlias, valueSchema, description). Defaults to the target type.', input_schema: { type: 'object', properties: { alias: { type: 'string' } } } },
    { name: 'get_block_element_types', description: 'List the block element types inside a Block List/Grid property, with their properties and nested blocks.', input_schema: { type: 'object', properties: { contentTypeAlias: { type: 'string' }, propertyAlias: { type: 'string' } }, required: ['propertyAlias'] } },
    { name: 'search_schema_types', description: 'Search Schema.org types by substring.', input_schema: { type: 'object', properties: { search: { type: 'string' } }, required: ['search'] } },
    { name: 'get_schema_type_properties', description: 'List a Schema.org type\'s properties. ranked=true returns Google-priority confidence + isPopular.', input_schema: { type: 'object', properties: { name: { type: 'string' }, ranked: { type: 'boolean' } }, required: ['name'] } },
    { name: 'suggest_property_mappings', description: 'Heuristic baseline suggestions (name-only) for the target type against a schema type. Improve on it.', input_schema: { type: 'object', properties: { schemaTypeName: { type: 'string' } }, required: ['schemaTypeName'] } },
    { name: 'save_schema_mapping', description: 'Persist the mapping for the target content type (replaces it wholesale).', input_schema: { type: 'object', properties: { schemaTypeName: { type: 'string' }, propertyMappings: { type: 'array', items: { type: 'object', properties: { schemaPropertyName: { type: 'string' }, sourceType: { type: 'string' }, contentTypePropertyAlias: { type: ['string', 'null'] }, sourceContentTypeAlias: { type: ['string', 'null'] }, transformType: { type: ['string', 'null'] }, staticValue: { type: ['string', 'null'] }, nestedSchemaTypeName: { type: ['string', 'null'] }, resolverConfig: { type: ['string', 'null'], description: 'JSON string: complexTypeMappings | nestedMappings | routes | extractAs stringList' }, targetPieceKey: { type: ['string', 'null'] } }, required: ['schemaPropertyName'] } } }, required: ['schemaTypeName', 'propertyMappings'] } },
    { name: 'preview_json_ld', description: 'Render the saved mapping to JSON-LD and report validation issues.', input_schema: { type: 'object', properties: {} } },
  ];
}

// Per-type tool handlers, closing over the target alias + content-type key. `state.lastSave`
// captures the model's final mapping for scoring.
function makeHandlers(alias, ctKey, state) {
  return {
    get_content_type_properties: ({ alias: a }) => api.get(`/content-types/${a || alias}/properties`),
    get_block_element_types: ({ contentTypeAlias, propertyAlias }) =>
      api.get(`/content-types/${contentTypeAlias || alias}/properties/${propertyAlias}/block-types`),
    search_schema_types: ({ search }) => api.get('/schema-types', { search: search ?? '' }),
    get_schema_type_properties: ({ name, ranked }) =>
      api.get(`/schema-types/${name}/properties`, ranked === false ? undefined : { ranked: 'true' }),
    suggest_property_mappings: ({ schemaTypeName }) =>
      api.post(`/mappings/${alias}/auto-map`, { schemaTypeName }),
    save_schema_mapping: async ({ schemaTypeName, propertyMappings }) => {
      const mappings = Array.isArray(propertyMappings) ? propertyMappings : [];
      state.lastSave = { schemaTypeName, propertyMappings: mappings };
      const dto = {
        contentTypeAlias: alias,
        contentTypeKey: ctKey,
        schemaTypeName,
        isEnabled: true,
        isInherited: false,
        propertyMappings: mappings.map((p) => ({
          schemaPropertyName: p.schemaPropertyName,
          sourceType: p.sourceType || 'property',
          contentTypePropertyAlias: p.contentTypePropertyAlias ?? null,
          sourceContentTypeAlias: p.sourceContentTypeAlias ?? null,
          transformType: p.transformType ?? null,
          isAutoMapped: false,
          staticValue: p.staticValue ?? null,
          nestedSchemaTypeName: p.nestedSchemaTypeName ?? null,
          resolverConfig: p.resolverConfig ?? null,
          targetPieceKey: p.targetPieceKey ?? null,
        })),
      };
      const res = await api.post('/mappings', undefined, dto);
      return { ok: true, reachability: res?.reachability, warnings: res?.warnings ?? [] };
    },
    preview_json_ld: async () => {
      try {
        const res = await api.post(`/mappings/${alias}/preview`, undefined, {});
        const jsonLd = typeof res?.jsonLd === 'string' ? res.jsonLd.slice(0, 1500) : res?.jsonLd;
        return { isValid: res?.isValid, issues: res?.issues ?? [], jsonLd };
      } catch (e) {
        return { error: String(e.message || e).slice(0, 200) };
      }
    },
  };
}

async function anthropic(body) {
  const res = await fetch('https://api.anthropic.com/v1/messages', {
    method: 'POST',
    headers: { 'x-api-key': getApiKey(), 'anthropic-version': '2023-06-01', 'content-type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`Anthropic ${res.status}: ${(await res.text()).slice(0, 400)}`);
  return res.json();
}

// Drive one full tool-use conversation for a single content type.
async function mapOne(alias, ctKey, schemaHint) {
  const state = { lastSave: null };
  const handlers = makeHandlers(alias, ctKey, state);
  const tools = toolDefs();
  const messages = [
    {
      role: 'user',
      content:
        `Map the Umbraco content type "${alias}" to Schema.org and persist it with save_schema_mapping. ` +
        `A strong candidate schema type is "${schemaHint}", but verify it is the most specific fit. ` +
        `Start by inspecting the content type.`,
    },
  ];

  for (let turn = 0; turn < MAX_TURNS; turn++) {
    const data = await anthropic({ model: MODEL, max_tokens: 4096, system: SYSTEM_TIER3, tools, messages });
    messages.push({ role: 'assistant', content: data.content });
    if (data.stop_reason !== 'tool_use') break;

    const results = [];
    for (const block of data.content) {
      if (block.type !== 'tool_use') continue;
      let out;
      try {
        const handler = handlers[block.name];
        out = handler ? await handler(block.input || {}) : { error: `unknown tool ${block.name}` };
      } catch (e) {
        out = { error: String(e.message || e).slice(0, 300) };
      }
      results.push({ type: 'tool_result', tool_use_id: block.id, content: JSON.stringify(out).slice(0, 8000) });
    }
    messages.push({ role: 'user', content: results });
  }
  return state.lastSave;
}

async function main() {
  const gold = loadAllGold();
  const sample = RICH.filter((s) => gold.get(s.alias.toLowerCase()));

  // alias -> content-type key, needed to persist a mapping
  const ctRes = await api.get('/content-types');
  const ctItems = Array.isArray(ctRes) ? ctRes : ctRes?.items ?? [];
  const keyByAlias = new Map(ctItems.map((c) => [String(c.alias).toLowerCase(), c.key ?? c.id ?? c.Key]));

  console.log(`Tier-3 MCP-driven eval (model ${MODEL}) over ${sample.length} rich types...\n`);

  const per = [];
  for (const s of sample) {
    const g = gold.get(s.alias.toLowerCase());
    const ctKey = keyByAlias.get(s.alias.toLowerCase());
    if (!ctKey) {
      console.warn(`  ! ${s.alias}: no content-type key; skipping`);
      continue;
    }

    // Snapshot the existing mapping, then clear it so the model maps from scratch.
    let snapshot = null;
    try {
      snapshot = await api.get(`/mappings/${s.alias}`);
    } catch {
      /* no existing mapping */
    }
    try {
      await api.del(`/mappings/${s.alias}`);
    } catch {
      /* nothing to delete */
    }

    let scored;
    try {
      const save = await mapOne(s.alias, ctKey, g.schemaType);
      scored = scoreOne(g, save?.propertyMappings ?? []);
      scored.tag = s.tag;
      per.push(scored);
      console.log(
        `  ${s.alias.padEnd(20)} rich ${scored.rich.hit}/${scored.rich.goldCount}  strictF1 ${scored.strict.f1}  (saved ${save?.propertyMappings?.length ?? 0} props -> ${save?.schemaTypeName ?? '?'})`,
      );
    } catch (e) {
      console.warn(`  ! ${s.alias}: ${String(e.message || e).slice(0, 200)}`);
    } finally {
      // Restore the original gold mapping (output-only DTO fields are ignored on input).
      try {
        if (snapshot) await api.post('/mappings', undefined, snapshot);
        else await api.del(`/mappings/${s.alias}`);
      } catch (e) {
        console.warn(`  ! restore ${s.alias} failed: ${String(e.message || e).slice(0, 160)}`);
      }
    }
  }

  const agg = aggregate(per);
  console.log('\n=== TIER-3 MCP-DRIVEN SUMMARY ===');
  console.log(`RICH coverage:  MCP ${pct(agg.richCoverage.pct)} (${agg.richCoverage.hit}/${agg.richCoverage.goldCount})   [satellite reference: ~83% (10/12)]`);
  console.log(`Strict F1:      MCP ${agg.strictF1_macro}   [satellite reference: ~0.68]`);
  console.log(`Lenient F1:     MCP ${agg.lenientF1_macro}`);
  console.log(`Avg props/type: ${agg.avgCandidates}`);
}

main();
