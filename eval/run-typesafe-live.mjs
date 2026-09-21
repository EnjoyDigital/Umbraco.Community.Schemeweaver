// Live eval runner for the TypeSafe satellite: scores the REAL C# package, as the backoffice
// and the MCP server see it, against gold. Modelled on eval/run-typesafe.mjs, but the
// suggestions come from the TestHost's auto-map endpoint instead of the JS mapper:
//
//   POST /umbraco/management/api/v1/schemeweaver/mappings/{alias}/auto-map?schemaTypeName=X
//
// This is the measurement for v2 (cross-node sources, per-block routes, multiple targets,
// value schema): those live only in the C# package, while eval/typesafe-mapper.mjs is the
// frozen v1 specification that eval/run-typesafe.mjs and eval/oracle.mjs exercise.
//
// Prerequisites: the TestHost on :44308 (see eval/live-client.mjs for the API user creds)
// with the TypeSafe satellite ACTIVE (a key in user-secrets; check the startup log line or
// the "SchemeWeaver TypeSafe" health check, because the endpoint does not say which mapper
// answered). Cost is not available live either (the endpoint hides the token usage), so the
// runner prints request timing instead.
//
// The heuristic comparison is a two-step dance, because the same endpoint IS the heuristic
// when the host has no key: run once with no key and --save-heuristic, then again with the
// key and --heuristic-file, and the second run prints the three-way summary and the
// priors-merge block exactly as run-typesafe.mjs does.
//
// Usage: node eval/run-typesafe-live.mjs [--label ts-live-v2] [--only recipePage,faqPage]
//        [--save-heuristic eval/reports/heuristic-live.json]
//        [--heuristic-file eval/reports/heuristic-live.json]

import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { loadAllGold } from './gold.mjs';
import { SAMPLE } from './sample.mjs';
import { scoreOne, aggregate } from './score.mjs';
import { api, BASE } from './live-client.mjs';

const REPO = process.cwd();
const REPORTS = join(REPO, 'eval/reports');

const arg = (name, fallback) => {
  const i = process.argv.indexOf(`--${name}`);
  return i > 0 ? process.argv[i + 1] : fallback;
};

const LABEL = arg('label', 'typesafe-live');
const ONLY = arg('only', null);
const HEURISTIC_FILE = arg('heuristic-file', null);
const SAVE_HEURISTIC = arg('save-heuristic', null);

// The package's merge rule, scored afterwards from the same rows as run-typesafe.mjs does
// (same rules, same floors, same function) so the two reports read side by side. One
// caveat the cached-context run does not have: the live rows are already gated at the
// core's show threshold, so TypeSafe rows below 60 are not available to the merge here.
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

/** The endpoint returns a flat PropertyMappingSuggestion[]; tolerate a wrapped shape too. */
const rowsOf = (res) => (Array.isArray(res) ? res : res?.items ?? []);

function loadHeuristic(path) {
  if (!path) return null;
  if (!existsSync(path)) {
    console.warn(`! --heuristic-file ${path} not found; running without the heuristic comparison`);
    return null;
  }
  try {
    return JSON.parse(readFileSync(path, 'utf8'));
  } catch (e) {
    console.warn(`! --heuristic-file ${path} is not valid JSON (${e.message}); running without it`);
    return null;
  }
}

async function main() {
  mkdirSync(REPORTS, { recursive: true });
  const gold = loadAllGold();
  const heuristic = loadHeuristic(HEURISTIC_FILE);

  let sample = SAMPLE.filter((s) => gold.get(s.alias.toLowerCase()));
  if (ONLY) {
    const want = new Set(ONLY.split(',').map((s) => s.trim().toLowerCase()));
    sample = sample.filter((s) => want.has(s.alias.toLowerCase()));
  }

  console.log(
    `Running LIVE TypeSafe eval "${LABEL}" against ${BASE} over ${sample.length} types` +
      (heuristic ? ` (heuristic rows from ${HEURISTIC_FILE})` : '') +
      (SAVE_HEURISTIC ? ` (saving the endpoint's rows as the heuristic to ${SAVE_HEURISTIC})` : '') +
      `...\n`,
  );
  console.log(
    'NOTE: the endpoint does not say which mapper answered. Confirm the satellite is active (or,\n' +
      'for --save-heuristic, inert) from the startup log line before trusting the numbers.\n',
  );

  const tsPer = [];
  const heuPer = [];
  const mergedPer = Object.fromEntries(PRIOR_RULES.map((r) => [r.key, []]));
  const raw = {};
  const timings = {};
  const failures = [];

  // Sequential: the TestHost runs on SQLite, and one mapping call is several TypeSafe
  // requests; concurrency here only adds lock contention, not speed.
  for (const s of sample) {
    const g = gold.get(s.alias.toLowerCase());

    let suggestions = [];
    const started = performance.now();
    try {
      suggestions = rowsOf(await api.post(`/mappings/${s.alias}/auto-map`, { schemaTypeName: g.schemaType }));
    } catch (e) {
      const msg = String(e.message || e).slice(0, 200);
      failures.push({ alias: s.alias, error: msg });
      console.warn(`  ! auto-map failed for ${s.alias}: ${msg}`);
    }
    const ms = Math.round(performance.now() - started);
    timings[s.alias] = ms;
    raw[s.alias] = suggestions;

    // Scored as the endpoint emits it: the package already gated at the core show threshold.
    const tsScore = scoreOne(g, suggestions);
    tsScore.tag = s.tag;
    tsScore._raw = suggestions;
    tsPer.push(tsScore);

    let heuLine = '';
    if (heuristic) {
      const heuRows = heuristic[s.alias] || [];
      const heuScore = scoreOne(g, heuRows);
      heuScore.tag = s.tag;
      heuPer.push(heuScore);
      heuLine = ` heu=${heuScore.strict.f1} (rich ${heuScore.rich.hit}/${heuScore.rich.goldCount}, cross ${heuScore.crossNode.hit}/${heuScore.crossNode.goldCount})`;
      for (const rule of PRIOR_RULES) {
        const merged = scoreOne(g, mergeWithPriors(heuRows, suggestions, rule.floor));
        merged.tag = s.tag;
        mergedPer[rule.key].push(merged);
      }
    }

    console.log(
      `  ${s.alias.padEnd(20)} [${s.tag}]  rich ${tsScore.rich.hit}/${tsScore.rich.goldCount}  ` +
        `cross ${tsScore.crossNode.hit}/${tsScore.crossNode.goldCount}  strictF1 ${tsScore.strict.f1}${heuLine}  ` +
        `${suggestions.length} rows  ${ms} ms`,
    );
    if (tsScore.rich.missed.length) console.log(`      missed rich:  ${tsScore.rich.missed.join(' | ')}`);
    if (tsScore.crossNode.missed.length) console.log(`      missed cross: ${tsScore.crossNode.missed.join(' | ')}`);
  }

  if (SAVE_HEURISTIC) {
    writeFileSync(SAVE_HEURISTIC, JSON.stringify(raw, null, 2));
    console.log(`\nSaved the endpoint's rows for ${Object.keys(raw).length} types to ${SAVE_HEURISTIC}`);
  }

  const tsAgg = aggregate(tsPer);
  const heuAgg = heuristic ? aggregate(heuPer) : null;
  const mergedAgg = heuristic ? Object.fromEntries(PRIOR_RULES.map((r) => [r.key, aggregate(mergedPer[r.key])])) : {};

  // The stored Anthropic and JS-mapper (v1) numbers for the wider comparison.
  const anthropic = previousSummary(join(REPORTS, 'latest.json'), (p) => p.summary?.ai);
  const jsV1 = previousSummary(join(REPORTS, 'latest-typesafe.json'), (p) => p.summary?.typesafe);

  const totalMs = Object.values(timings).reduce((a, b) => a + b, 0);
  const report = {
    label: LABEL,
    leg: 'typesafe-live',
    host: BASE,
    heuristicFile: HEURISTIC_FILE,
    sampleSize: sample.length,
    timing: { totalMs, avgMs: Math.round(totalMs / (sample.length || 1)), perType: timings },
    failures,
    summary: {
      typesafe: tsAgg,
      ...(heuAgg ? { heuristic: heuAgg } : {}),
      ...mergedAgg,
      ...(anthropic ? { anthropic: anthropic.summary } : {}),
      ...(jsV1 ? { typesafeJsV1: jsV1.summary } : {}),
    },
    anthropicRef: anthropic ? { label: anthropic.label, model: anthropic.model } : null,
    jsV1Ref: jsV1 ? { label: jsV1.label, model: jsV1.model } : null,
    delta: heuAgg
      ? {
          richCoveragePct: round(tsAgg.richCoverage.pct - heuAgg.richCoverage.pct),
          crossNodeCoveragePct: round(tsAgg.crossNodeCoverage.pct - heuAgg.crossNodeCoverage.pct),
          strictF1_macro: round(tsAgg.strictF1_macro - heuAgg.strictF1_macro),
          lenientF1_macro: round(tsAgg.lenientF1_macro - heuAgg.lenientF1_macro),
        }
      : null,
    perType: tsPer.map((a) => {
      const h = heuPer.find((x) => x.alias === a.alias);
      return {
        alias: a.alias,
        tag: a.tag,
        schemaType: a.schemaType,
        ms: timings[a.alias],
        typesafe: { strictF1: a.strict.f1, lenientF1: a.lenient.f1, rich: a.rich, crossNode: a.crossNode },
        ...(h ? { heuristic: { strictF1: h.strict.f1, lenientF1: h.lenient.f1, rich: h.rich, crossNode: h.crossNode } } : {}),
        typesafeRaw: a._raw,
      };
    }),
  };

  const stamp = new Date().toISOString().replace(/[:.]/g, '-');
  const path = join(REPORTS, `${LABEL}-${stamp}.json`);
  writeFileSync(path, JSON.stringify(report, null, 2));
  writeFileSync(join(REPORTS, 'latest-typesafe-live.json'), JSON.stringify(report, null, 2));

  console.log('\n=== SUMMARY (live C# package) ===');
  console.log(
    `Self-contained RICH coverage:  TypeSafe ${pct(tsAgg.richCoverage.pct)} (${tsAgg.richCoverage.hit}/${tsAgg.richCoverage.goldCount})` +
      (heuAgg ? `   vs   heuristic ${pct(heuAgg.richCoverage.pct)} (${heuAgg.richCoverage.hit}/${heuAgg.richCoverage.goldCount})` : '') +
      (anthropic ? `   vs   Anthropic ${pct(anthropic.summary.richCoverage.pct)}` : ''),
  );
  console.log(
    `Cross-node coverage:           TypeSafe ${pct(tsAgg.crossNodeCoverage.pct)} (${tsAgg.crossNodeCoverage.hit}/${tsAgg.crossNodeCoverage.goldCount})` +
      (heuAgg ? `   vs   heuristic ${pct(heuAgg.crossNodeCoverage.pct)} (${heuAgg.crossNodeCoverage.hit}/${heuAgg.crossNodeCoverage.goldCount})` : '') +
      (anthropic?.summary.crossNodeCoverage ? `   vs   Anthropic ${pct(anthropic.summary.crossNodeCoverage.pct)}` : ''),
  );
  console.log(
    `Strict F1 (macro):             TypeSafe ${tsAgg.strictF1_macro}` +
      (heuAgg ? `   vs   heuristic ${heuAgg.strictF1_macro}` : '') +
      (anthropic ? `   vs   Anthropic ${anthropic.summary.strictF1_macro}` : ''),
  );
  console.log(
    `Lenient F1 (macro):            TypeSafe ${tsAgg.lenientF1_macro}` +
      (heuAgg ? `   vs   heuristic ${heuAgg.lenientF1_macro}` : '') +
      (anthropic ? `   vs   Anthropic ${anthropic.summary.lenientF1_macro}` : ''),
  );
  console.log(
    `Avg candidates per type:       TypeSafe ${tsAgg.avgCandidates}` + (heuAgg ? `   vs   heuristic ${heuAgg.avgCandidates}` : ''),
  );
  if (jsV1) {
    console.log(
      `JS mapper v1 (${jsV1.label}, cached context, for reference): rich ${pct(jsV1.summary.richCoverage.pct)} ` +
        `strictF1 ${jsV1.summary.strictF1_macro}   lenientF1 ${jsV1.summary.lenientF1_macro}`,
    );
  }

  if (heuAgg) {
    console.log("\n=== WITH HEURISTIC PRIORS (the package's merge rule, from the same live rows) ===");
    for (const rule of PRIOR_RULES) {
      const m = mergedAgg[rule.key];
      console.log(
        `${rule.label.padEnd(30)} rich ${pct(m.richCoverage.pct)} (${m.richCoverage.hit}/${m.richCoverage.goldCount})   ` +
          `strictF1 ${m.strictF1_macro}   lenientF1 ${m.lenientF1_macro}   avg candidates ${m.avgCandidates}`,
      );
    }
  } else {
    console.log(
      '\n(no --heuristic-file: run once against a host with NO TypeSafe key and --save-heuristic <path>,\n' +
        ' then pass that file here for the three-way summary and the priors-merge block)',
    );
  }

  if (failures.length) console.log(`\n! ${failures.length} auto-map call(s) failed; those types scored as empty.`);

  console.log(
    `\nTiming: ${sample.length} auto-map calls, ${(totalMs / 1000).toFixed(1)} s total, ` +
      `${report.timing.avgMs} ms per content type (cost is not exposed by the endpoint)`,
  );
  console.log(`Report: ${path}`);
}

/** A previous report's summary block, or null (a missing or malformed report never fails this run). */
function previousSummary(path, select) {
  if (!existsSync(path)) return null;
  try {
    const prev = JSON.parse(readFileSync(path, 'utf8'));
    const summary = select(prev);
    return summary ? { label: prev.label, model: prev.model, summary } : null;
  } catch {
    return null;
  }
}

const round = (n) => Math.round(n * 1000) / 1000;
const pct = (n) => `${Math.round(n * 100)}%`;

main();
