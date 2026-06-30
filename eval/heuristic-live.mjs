// Tier-2.5: score the LIVE heuristic auto-mapper against gold.
//
// run.mjs scores a CACHED heuristic baseline (eval/cache/context.json), which does not reflect a
// rebuilt C# mapper. After changing SchemaAutoMapper / BlockSchemaSuggester, this hits the live
// /mappings/{alias}/auto-map endpoint so the new heuristic is measured honestly, and compares it
// against the cached pre-change baseline so the rich lift and any strict-F1 regression are explicit.
//
// IMPORTANT: the AI satellite globally overrides ISchemaAutoMapper, so /auto-map would be the AI
// when the satellite is installed. Run this against a PLAIN TestHost (no AI satellite) so the
// endpoint is the real heuristic. Default build (Umbraco 18) compiles the satellite out.
//
// Usage: node eval/heuristic-live.mjs
import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { loadAllGold } from './gold.mjs';
import { SAMPLE } from './sample.mjs';
import { scoreOne, aggregate } from './score.mjs';
import { api } from './live-client.mjs';

const CACHE = join(process.cwd(), 'eval/cache/context.json');
const CACHED = existsSync(CACHE) ? JSON.parse(readFileSync(CACHE, 'utf8')) : {};
const pct = (n) => `${Math.round(n * 100)}%`;

async function main() {
  const gold = loadAllGold();
  const sample = SAMPLE.filter((s) => gold.get(s.alias.toLowerCase()));
  console.log(`Scoring LIVE heuristic over ${sample.length} types (satellite must NOT be installed)...\n`);

  const livePer = [];
  const basePer = [];
  for (const s of sample) {
    const g = gold.get(s.alias.toLowerCase());
    try {
      const live = await api.post(`/mappings/${s.alias}/auto-map`, { schemaTypeName: g.schemaType });
      const suggestions = Array.isArray(live) ? live : live?.items ?? [];
      const base = CACHED[s.alias]?.heuristicBaseline || [];
      const lv = scoreOne(g, suggestions);
      const bs = scoreOne(g, base);
      lv.tag = bs.tag = s.tag;
      livePer.push(lv);
      basePer.push(bs);
      console.log(
        `  ${s.alias.padEnd(20)} [${s.tag}] rich live ${lv.rich.hit}/${lv.rich.goldCount} (was ${bs.rich.hit}/${bs.rich.goldCount})  strictF1 ${lv.strict.f1} (was ${bs.strict.f1})`,
      );
    } catch (e) {
      console.warn(`  ! ${s.alias}: ${String(e.message || e).slice(0, 160)}`);
    }
  }

  const live = aggregate(livePer);
  const base = aggregate(basePer);
  console.log('\n=== LIVE HEURISTIC (satellite NOT installed) ===');
  console.log(
    `RICH coverage:  live ${pct(live.richCoverage.pct)} (${live.richCoverage.hit}/${live.richCoverage.goldCount})  vs  cached-baseline ${pct(base.richCoverage.pct)} (${base.richCoverage.hit}/${base.richCoverage.goldCount})`,
  );
  console.log(`Strict F1:      live ${live.strictF1_macro}  vs  baseline ${base.strictF1_macro}`);
  console.log(`Lenient F1:     live ${live.lenientF1_macro}  vs  baseline ${base.lenientF1_macro}`);

  const richLift = live.richCoverage.hit - base.richCoverage.hit;
  const regressed = live.strictF1_macro < base.strictF1_macro - 0.001;
  console.log(
    `\nGATE: rich lift ${richLift >= 0 ? '+' + richLift + ' OK' : richLift + ' FAIL'},  strict-F1 regression ${regressed ? 'FAIL' : 'OK'}`,
  );
  process.exit(regressed ? 1 : 0);
}

main();
