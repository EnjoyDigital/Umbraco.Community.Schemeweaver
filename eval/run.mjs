// Eval runner: scores the candidate prompt (AI) vs the heuristic baseline vs gold.
//
// Reads cached context (eval/cache/context.json) + gold, renders the candidate prompt per
// sampled content type, calls Anthropic, parses + scores, and writes a report to
// eval/reports/. Context is cached so iterating the prompt needs no Umbraco round-trips.
//
// Usage: node eval/run.mjs [--label v1]

import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { loadAllGold } from './gold.mjs';
import { SAMPLE } from './sample.mjs';
import { scoreOne, aggregate } from './score.mjs';
import { complete, extractJsonArray, MODEL } from './anthropic.mjs';
import { SYSTEM, buildUser, PROMPT_VERSION } from './prompt.mjs';

const REPO = process.cwd();
const CACHE = join(REPO, 'eval/cache/context.json');
const REPORTS = join(REPO, 'eval/reports');

const labelArg = process.argv.indexOf('--label');
const LABEL = labelArg > 0 ? process.argv[labelArg + 1] : PROMPT_VERSION;

async function mapWithLimit(items, limit, fn) {
  const out = new Array(items.length);
  let i = 0;
  async function worker() {
    while (i < items.length) {
      const idx = i++;
      out[idx] = await fn(items[idx], idx);
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
  return out;
}

async function aiSuggest(ctx, attempt = 0) {
  try {
    const text = await complete({ system: SYSTEM, user: buildUser(ctx), maxTokens: 4096 });
    return extractJsonArray(text);
  } catch (e) {
    if (attempt < 2) return aiSuggest(ctx, attempt + 1);
    console.warn(`  ! AI failed for ${ctx.alias}: ${String(e.message || e).slice(0, 160)}`);
    return [];
  }
}

async function main() {
  mkdirSync(REPORTS, { recursive: true });
  const context = JSON.parse(readFileSync(CACHE, 'utf8'));
  const gold = loadAllGold();

  const sample = SAMPLE.filter((s) => context[s.alias] && gold.get(s.alias.toLowerCase()));
  console.log(`Running eval "${LABEL}" (model ${MODEL}) over ${sample.length} types...\n`);

  const aiPer = [];
  const heuPer = [];

  await mapWithLimit(sample, 4, async (s) => {
    const ctx = context[s.alias];
    const g = gold.get(s.alias.toLowerCase());
    const ai = await aiSuggest(ctx);
    const aiScore = scoreOne(g, ai);
    const heuScore = scoreOne(g, ctx.heuristicBaseline || []);
    aiScore.tag = heuScore.tag = s.tag;
    aiPer.push(aiScore);
    heuPer.push(heuScore);
    aiScore._raw = ai; // keep for the devil's-advocate pass
    const rd = `rich ${aiScore.rich.hit}/${aiScore.rich.goldCount} (heu ${heuScore.rich.hit}/${heuScore.rich.goldCount})`;
    console.log(
      `  ${s.alias.padEnd(20)} [${s.tag}]  strictF1 ai=${aiScore.strict.f1} heu=${heuScore.strict.f1}  ${rd}`,
    );
  });

  const aiAgg = aggregate(aiPer);
  const heuAgg = aggregate(heuPer);

  const report = {
    label: LABEL,
    model: MODEL,
    promptVersion: PROMPT_VERSION,
    sampleSize: sample.length,
    summary: { ai: aiAgg, heuristic: heuAgg },
    delta: {
      richCoveragePct: round(aiAgg.richCoverage.pct - heuAgg.richCoverage.pct),
      strictF1_macro: round(aiAgg.strictF1_macro - heuAgg.strictF1_macro),
      lenientF1_macro: round(aiAgg.lenientF1_macro - heuAgg.lenientF1_macro),
    },
    perType: aiPer.map((a) => {
      const h = heuPer.find((x) => x.alias === a.alias);
      return {
        alias: a.alias,
        tag: a.tag,
        schemaType: a.schemaType,
        ai: { strictF1: a.strict.f1, lenientF1: a.lenient.f1, rich: a.rich, crossNode: a.crossNode },
        heuristic: { strictF1: h.strict.f1, lenientF1: h.lenient.f1, rich: h.rich, crossNode: h.crossNode },
        aiRaw: a._raw,
      };
    }),
  };

  const stamp = new Date().toISOString().replace(/[:.]/g, '-');
  const path = join(REPORTS, `${LABEL}-${stamp}.json`);
  writeFileSync(path, JSON.stringify(report, null, 2));
  writeFileSync(join(REPORTS, 'latest.json'), JSON.stringify(report, null, 2));

  console.log('\n=== SUMMARY ===');
  console.log(
    `Self-contained RICH coverage:  AI ${pct(aiAgg.richCoverage.pct)} (${aiAgg.richCoverage.hit}/${aiAgg.richCoverage.goldCount})   vs   heuristic ${pct(heuAgg.richCoverage.pct)} (${heuAgg.richCoverage.hit}/${heuAgg.richCoverage.goldCount})`,
  );
  console.log(`Strict F1 (macro):             AI ${aiAgg.strictF1_macro}   vs   heuristic ${heuAgg.strictF1_macro}`);
  console.log(`Lenient F1 (macro):            AI ${aiAgg.lenientF1_macro}   vs   heuristic ${heuAgg.lenientF1_macro}`);
  console.log(`Cross-node coverage:           AI ${aiAgg.crossNodeCoverage.hit}/${aiAgg.crossNodeCoverage.goldCount}   vs   heuristic ${heuAgg.crossNodeCoverage.hit}/${heuAgg.crossNodeCoverage.goldCount}`);
  console.log(`\nReport: ${path}`);
}

const round = (n) => Math.round(n * 1000) / 1000;
const pct = (n) => `${Math.round(n * 100)}%`;

main();
