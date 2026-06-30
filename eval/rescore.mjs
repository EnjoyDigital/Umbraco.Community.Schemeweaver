// Re-score saved report aiRaw with the CURRENT scorer (no API calls). Lets us compare
// prompt versions under a changed scorer deterministically.
//
// Usage: node eval/rescore.mjs reports/v1-....json reports/v2-....json [...]

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { loadAllGold } from './gold.mjs';
import { scoreOne, aggregate } from './score.mjs';

const REPO = process.cwd();
const context = JSON.parse(readFileSync(join(REPO, 'eval/cache/context.json'), 'utf8'));
const gold = loadAllGold();
const pct = (n) => `${Math.round(n * 100)}%`;

for (const arg of process.argv.slice(2)) {
  const path = arg.includes('/') || arg.includes('\\') ? arg : join(REPO, 'eval/reports', arg);
  const report = JSON.parse(readFileSync(path, 'utf8'));
  const aiPer = [];
  const heuPer = [];
  for (const row of report.perType) {
    const g = gold.get(row.alias.toLowerCase());
    if (!g) continue;
    const a = scoreOne(g, row.aiRaw || []);
    const h = scoreOne(g, context[row.alias]?.heuristicBaseline || []);
    a.tag = h.tag = row.tag;
    aiPer.push(a);
    heuPer.push(h);
  }
  const ai = aggregate(aiPer);
  const heu = aggregate(heuPer);
  console.log(`\n=== ${report.label} (re-scored) — ${report.perType.length} types ===`);
  console.log(`RICH coverage:  AI ${pct(ai.richCoverage.pct)} (${ai.richCoverage.hit}/${ai.richCoverage.goldCount})  vs  heu ${pct(heu.richCoverage.pct)} (${heu.richCoverage.hit}/${heu.richCoverage.goldCount})`);
  console.log(`Strict F1:      AI ${ai.strictF1_macro}  vs  heu ${heu.strictF1_macro}   (avg cand: AI ${ai.avgCandidates} / heu ${heu.avgCandidates})`);
  console.log(`Lenient F1:     AI ${ai.lenientF1_macro}  vs  heu ${heu.lenientF1_macro}`);
  // show rich hits/misses per type for the AI
  for (const a of aiPer.filter((x) => x.rich.goldCount)) {
    console.log(`   ${a.alias.padEnd(18)} rich ${a.rich.hit}/${a.rich.goldCount}${a.rich.missed.length ? '  missed: ' + a.rich.missed.join(', ') : ''}`);
  }
}
