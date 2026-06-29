// Tier-2 in-product verification: calls the LIVE satellite endpoints (which now run the
// ported v3 prompt + structural fixes, using the in-DB Anthropic key) and scores the AI
// vs the heuristic against gold with the SAME scorer used in Tier-1. This proves the
// improvement holds through the real Umbraco path, not just the standalone harness.
//
// Requires: rebuilt satellite + TestHost running on :44308, MCP API user provisioned.
// Usage: node eval/tier2-verify.mjs

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { loadAllGold } from './gold.mjs';
import { SAMPLE } from './sample.mjs';
import { scoreOne, aggregate } from './score.mjs';

// The satellite globally overrides ISchemaAutoMapper, so the live /mappings/auto-map
// endpoint is AI too — not a real heuristic. Use the cached TRUE heuristic baseline
// (captured before the AI was working) for an honest in-product AI-vs-heuristic delta.
const CACHED = JSON.parse(readFileSync(join(process.cwd(), 'eval/cache/context.json'), 'utf8'));

const BASE = 'https://localhost:44308';
const MGMT = `${BASE}/umbraco/management/api/v1`;
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

function creds() {
  const env = readFileSync(join(process.cwd(), 'src/Umbraco.Community.SchemeWeaver.Mcp/.env'), 'utf8');
  const get = (k) => (env.match(new RegExp(`^${k}=(.*)$`, 'm')) || [])[1]?.trim();
  return { id: get('UMBRACO_CLIENT_ID'), secret: get('UMBRACO_CLIENT_SECRET') };
}

async function token() {
  const { id, secret } = creds();
  const r = await fetch(`${MGMT}/security/back-office/token`, {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ grant_type: 'client_credentials', client_id: id, client_secret: secret }),
  });
  if (!r.ok) throw new Error(`token ${r.status}: ${await r.text()}`);
  return (await r.json()).access_token;
}

// Fetch a FRESH token per call — backoffice client-credentials tokens are short-lived
// and the AI calls are slow enough that one token expires mid-run.
async function post(path) {
  const tok = await token();
  const r = await fetch(`${MGMT}/schemeweaver${path}`, {
    method: 'POST',
    headers: { authorization: `Bearer ${tok}`, 'content-type': 'application/json' },
    body: '{}',
  });
  if (!r.ok) throw new Error(`${path} -> ${r.status}: ${(await r.text()).slice(0, 160)}`);
  return r.json();
}

const pct = (n) => `${Math.round(n * 100)}%`;

async function main() {
  const gold = loadAllGold();
  const sample = SAMPLE.filter((s) => gold.get(s.alias.toLowerCase()));

  const aiPer = [];
  const heuPer = [];
  for (const s of sample) {
    const g = gold.get(s.alias.toLowerCase());
    const q = `?schemaTypeName=${encodeURIComponent(g.schemaType)}`;
    try {
      const ai = await post(`/ai/ai-auto-map/${s.alias}${q}`);
      const heu = CACHED[s.alias]?.heuristicBaseline || []; // true heuristic, from cache
      const a = scoreOne(g, ai);
      const h = scoreOne(g, heu);
      a.tag = h.tag = s.tag;
      aiPer.push(a);
      heuPer.push(h);
      console.log(`  ${s.alias.padEnd(20)} [${s.tag}] rich AI ${a.rich.hit}/${a.rich.goldCount} heu ${h.rich.hit}/${h.rich.goldCount}  strictF1 ai=${a.strict.f1} heu=${h.strict.f1}`);
    } catch (e) {
      console.warn(`  ! ${s.alias}: ${String(e.message || e).slice(0, 160)}`);
    }
  }

  const ai = aggregate(aiPer);
  const heu = aggregate(heuPer);
  console.log('\n=== TIER-2 IN-PRODUCT SUMMARY (live satellite endpoint) ===');
  console.log(`RICH coverage:  AI ${pct(ai.richCoverage.pct)} (${ai.richCoverage.hit}/${ai.richCoverage.goldCount})  vs  heuristic ${pct(heu.richCoverage.pct)} (${heu.richCoverage.hit}/${heu.richCoverage.goldCount})`);
  console.log(`Strict F1:      AI ${ai.strictF1_macro}  vs  heuristic ${heu.strictF1_macro}`);
  console.log(`Lenient F1:     AI ${ai.lenientF1_macro}  vs  heuristic ${heu.lenientF1_macro}`);
}

main();
