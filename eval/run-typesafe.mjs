// Eval runner for the TypeSafe leg: scores Jev's composed judgments vs the heuristic
// baseline vs gold, reusing the existing scorer so the numbers are directly comparable
// to the Anthropic reports already in eval/reports/.
//
// Reads the same cached context (eval/cache/context.json) plus the Schema.org registry
// dump (eval/cache/schema-types.json), so it needs no Umbraco round-trips.
//
// Usage: node eval/run-typesafe.mjs [--label ts-v1] [--limit 60] [--min-confidence 0]
//        [--only recipePage,faqPage]

import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { loadAllGold } from './gold.mjs';
import { SAMPLE } from './sample.mjs';
import { scoreOne, aggregate } from './score.mjs';
import { typeSafeSuggest, DEFAULT_TARGET_LIMIT } from './typesafe-mapper.mjs';
import { MODEL, usage, usdSpent } from './typesafe.mjs';

const REPO = process.cwd();
const CACHE = join(REPO, 'eval/cache/context.json');
const REPORTS = join(REPO, 'eval/reports');

const arg = (name, fallback) => {
  const i = process.argv.indexOf(`--${name}`);
  return i > 0 ? process.argv[i + 1] : fallback;
};

const LABEL = arg('label', 'typesafe-v1');
const TARGET_LIMIT = Number(arg('limit', DEFAULT_TARGET_LIMIT));
const MIN_CONFIDENCE = Number(arg('min-confidence', 0));
const ONLY = arg('only', null);

// The package does not run TypeSafe on its own: it merges the heuristic's suggestions in as
// PRIORS (TypeSafePropertyMapper.MergeWithPriors). That rule was a design decision, not
// something this harness ever measured, so every run now also scores the merged result under
// each candidate rule, from the SAME TypeSafe answers (no extra tokens). A prior survives when
// it is not a plain `property` row and its confidence is at or above the rule's floor (the
// heuristic's popular-defaults shapes sit at 60, its references at 70-90, its enriched rich
// rows at 80-100), or when it is an exact-alias `property` match (100). TypeSafe rows for the
// kept schema properties are dropped, the rest are added, and the core's show threshold gates.
// NOTE: the cached heuristicBaseline predates the 17.10.0 range-aware enricher uplift.
const AUTO_APPLY = 80;
const SHOW = 60;
const PRIOR_RULES = [
  { key: 'mergedAtAutoApply', label: 'merged (priors >= 80)', floor: AUTO_APPLY },
  { key: 'mergedAtShow', label: 'merged (priors >= 60)', floor: SHOW },
];

function mergeWithPriors(priors, tsRows, floor) {
  const conf = (s) => s.confidence ?? 0;
  const source = (s) => (s.suggestedSourceType ?? 'property').toLowerCase();
  const kept = priors.filter((p) => (source(p) !== 'property' ? conf(p) >= floor : conf(p) === 100));
  const keptNames = new Set(kept.map((p) => p.schemaPropertyName.toLowerCase()));
  const rest = tsRows
    .filter((r) => !keptNames.has(r.schemaPropertyName.toLowerCase()))
    .sort((a, b) => conf(b) - conf(a));
  return [...kept, ...rest].filter((s) => conf(s) >= SHOW);
}

async function mapWithLimit(items, limit, fn) {
  const out = new Array(items.length);
  let i = 0;
  async function worker() {
    while (i < items.length) {
      const idx = i++;
      out[idx] = await fn(items[idx]);
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
  return out;
}

/**
 * Recall ceiling diagnostic: how many of this type's gold schema properties were even
 * offered as options in round 1. A gold target outside the offered set CANNOT be hit,
 * so a low number here means the target limit is the bottleneck, not the model.
 */
function offeredCoverage(ctx, gold, limit) {
  const offered = new Set(
    (ctx.rankedSchemaProperties || []).slice(0, limit).map((p) => p.name.toLowerCase()),
  );
  const targets = gold.mappings.filter((m) => m.schemaProp).map((m) => m.schemaProp.toLowerCase());
  const missing = targets.filter((t) => !offered.has(t));
  return { goldCount: targets.length, offered: targets.length - missing.length, missing };
}

async function main() {
  mkdirSync(REPORTS, { recursive: true });
  const context = JSON.parse(readFileSync(CACHE, 'utf8'));
  const gold = loadAllGold();

  let sample = SAMPLE.filter((s) => context[s.alias] && gold.get(s.alias.toLowerCase()));
  if (ONLY) {
    const want = new Set(ONLY.split(',').map((s) => s.trim().toLowerCase()));
    sample = sample.filter((s) => want.has(s.alias.toLowerCase()));
  }

  console.log(
    `Running TypeSafe eval "${LABEL}" (model ${MODEL}, targetLimit=${TARGET_LIMIT}, ` +
      `minConfidence=${MIN_CONFIDENCE}) over ${sample.length} types...\n`,
  );

  const tsPer = [];
  const heuPer = [];
  const mergedPer = Object.fromEntries(PRIOR_RULES.map((r) => [r.key, []]));
  const ceilings = {};
  const traces = {};

  // Concurrency 3: well inside the 1,200 req/min limit while keeping the run brisk.
  await mapWithLimit(sample, 3, async (s) => {
    const ctx = context[s.alias];
    const g = gold.get(s.alias.toLowerCase());
    ceilings[s.alias] = offeredCoverage(ctx, g, TARGET_LIMIT);

    let suggestions = [];
    try {
      const res = await typeSafeSuggest(ctx, {
        targetLimit: TARGET_LIMIT,
        minConfidence: MIN_CONFIDENCE,
      });
      suggestions = res.suggestions;
      traces[s.alias] = res.trace;
    } catch (e) {
      console.warn(`  ! TypeSafe failed for ${s.alias}: ${String(e.message || e).slice(0, 200)}`);
    }

    // Scored as the package emits it: the core drops rows below its show threshold before
    // anything reaches the UI, and that gating is worth about +0.02 strict F1 on its own.
    const tsScore = scoreOne(g, suggestions.filter((s) => (s.confidence ?? 0) >= SHOW));
    const heuScore = scoreOne(g, ctx.heuristicBaseline || []);
    tsScore.tag = heuScore.tag = s.tag;
    tsScore._raw = suggestions;
    tsPer.push(tsScore);
    heuPer.push(heuScore);
    for (const rule of PRIOR_RULES) {
      const merged = scoreOne(g, mergeWithPriors(ctx.heuristicBaseline || [], suggestions, rule.floor));
      merged.tag = s.tag;
      mergedPer[rule.key].push(merged);
    }

    console.log(
      `  ${s.alias.padEnd(20)} [${s.tag}]  strictF1 ts=${tsScore.strict.f1} heu=${heuScore.strict.f1}  ` +
        `rich ${tsScore.rich.hit}/${tsScore.rich.goldCount} (heu ${heuScore.rich.hit}/${heuScore.rich.goldCount})`,
    );
    if (tsScore.rich.missed.length) console.log(`      missed rich: ${tsScore.rich.missed.join(' | ')}`);
  });

  const tsAgg = aggregate(tsPer);
  const heuAgg = aggregate(heuPer);
  const mergedAgg = Object.fromEntries(PRIOR_RULES.map((r) => [r.key, aggregate(mergedPer[r.key])]));

  // Pull the stored Anthropic numbers for a three-way view when they cover the same sample.
  let anthropic = null;
  const latest = join(REPORTS, 'latest.json');
  if (existsSync(latest)) {
    try {
      const prev = JSON.parse(readFileSync(latest, 'utf8'));
      if (prev.summary?.ai) anthropic = { label: prev.label, model: prev.model, summary: prev.summary.ai };
    } catch {
      /* a malformed previous report must not fail this run */
    }
  }

  const report = {
    label: LABEL,
    leg: 'typesafe',
    model: MODEL,
    targetLimit: TARGET_LIMIT,
    minConfidence: MIN_CONFIDENCE,
    sampleSize: sample.length,
    usage: { ...usage, usd: Number(usdSpent().toFixed(6)) },
    summary: { typesafe: tsAgg, heuristic: heuAgg, ...mergedAgg, ...(anthropic ? { anthropic: anthropic.summary } : {}) },
    anthropicRef: anthropic ? { label: anthropic.label, model: anthropic.model } : null,
    delta: {
      richCoveragePct: round(tsAgg.richCoverage.pct - heuAgg.richCoverage.pct),
      strictF1_macro: round(tsAgg.strictF1_macro - heuAgg.strictF1_macro),
      lenientF1_macro: round(tsAgg.lenientF1_macro - heuAgg.lenientF1_macro),
    },
    recallCeiling: ceilings,
    perType: tsPer.map((a) => {
      const h = heuPer.find((x) => x.alias === a.alias);
      return {
        alias: a.alias,
        tag: a.tag,
        schemaType: a.schemaType,
        typesafe: { strictF1: a.strict.f1, lenientF1: a.lenient.f1, rich: a.rich, crossNode: a.crossNode },
        heuristic: { strictF1: h.strict.f1, lenientF1: h.lenient.f1, rich: h.rich, crossNode: h.crossNode },
        typesafeRaw: a._raw,
      };
    }),
  };

  const stamp = new Date().toISOString().replace(/[:.]/g, '-');
  const path = join(REPORTS, `${LABEL}-${stamp}.json`);
  writeFileSync(path, JSON.stringify(report, null, 2));
  writeFileSync(join(REPORTS, 'latest-typesafe.json'), JSON.stringify(report, null, 2));
  writeFileSync(join(REPORTS, `${LABEL}-${stamp}.trace.json`), JSON.stringify(traces, null, 2));

  console.log('\n=== SUMMARY ===');
  console.log(
    `Self-contained RICH coverage:  TypeSafe ${pct(tsAgg.richCoverage.pct)} ` +
      `(${tsAgg.richCoverage.hit}/${tsAgg.richCoverage.goldCount})   vs   ` +
      `heuristic ${pct(heuAgg.richCoverage.pct)} (${heuAgg.richCoverage.hit}/${heuAgg.richCoverage.goldCount})` +
      (anthropic ? `   vs   Anthropic ${pct(anthropic.summary.richCoverage.pct)}` : ''),
  );
  console.log(
    `Strict F1 (macro):             TypeSafe ${tsAgg.strictF1_macro}   vs   heuristic ${heuAgg.strictF1_macro}` +
      (anthropic ? `   vs   Anthropic ${anthropic.summary.strictF1_macro}` : ''),
  );
  console.log(
    `Lenient F1 (macro):            TypeSafe ${tsAgg.lenientF1_macro}   vs   heuristic ${heuAgg.lenientF1_macro}` +
      (anthropic ? `   vs   Anthropic ${anthropic.summary.lenientF1_macro}` : ''),
  );
  console.log(
    `Avg candidates per type:       TypeSafe ${tsAgg.avgCandidates}   vs   heuristic ${heuAgg.avgCandidates}`,
  );
  console.log('\n=== WITH HEURISTIC PRIORS (the package\'s merge rule, from the same TypeSafe answers) ===');
  for (const rule of PRIOR_RULES) {
    const m = mergedAgg[rule.key];
    console.log(
      `${rule.label.padEnd(30)} rich ${pct(m.richCoverage.pct)} (${m.richCoverage.hit}/${m.richCoverage.goldCount})   ` +
        `strictF1 ${m.strictF1_macro}   lenientF1 ${m.lenientF1_macro}   avg candidates ${m.avgCandidates}`,
    );
  }

  const ceilMissing = Object.entries(ceilings).filter(([, c]) => c.missing.length);
  if (ceilMissing.length) {
    console.log(`\n! Gold targets never offered as options (recall ceiling, raise --limit):`);
    for (const [alias, c] of ceilMissing) console.log(`    ${alias}: ${c.missing.join(', ')}`);
  }

  console.log(
    `\nCost: ${usage.requests} requests, ${usage.inputTokens.toLocaleString()} input tokens, ` +
      `$${usdSpent().toFixed(4)} (~$${((usdSpent() / (sample.length || 1))).toFixed(5)}/content type)`,
  );
  console.log(`Report: ${path}`);
}

const round = (n) => Math.round(n * 1000) / 1000;
const pct = (n) => `${Math.round(n * 100)}%`;

main();
