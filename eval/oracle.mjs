// Self-test for the TypeSafe eval leg — no API key, no network.
//
// Stubs globalThis.fetch with a "gold oracle": a hypothetical perfect model that always
// picks the answer gold implies. The oracle's score is the CEILING of this harness — the
// best any model could score through it. A real run must be read against that ceiling,
// not against 1.0, and if the ceiling drops, the harness (question design, assembly or
// scoring integration) has broken rather than the model.
//
// Current ceiling: rich 11/12, strict F1 0.906. The shortfall is deliberate scope:
//   - nested-block `routes` (nestedBlocksPage.hasPart) is not attempted
//   - cross-node sources (ancestor/parent/sibling, 6 rows) are not attempted: the cached
//     context carries no information about surrounding content types
//   - gold maps a few single content properties to TWO schema properties (newsArticle
//     title -> Headline AND Name); the content->schema binding direction emits one
//
// Usage:  node eval/oracle.mjs [oracle|null]
//   oracle  perfect answers -> the ceiling
//   null    everything __none -> graceful-degradation check (must be 0 with no crash)

process.env.TYPESAFE_API_KEY = 'dry-run-not-used';

const { loadAllGold } = await import('./gold.mjs');
const { SAMPLE } = await import('./sample.mjs');
const { scoreOne, aggregate } = await import('./score.mjs');
const { isSubtypeOf } = await import('./schema-graph.mjs');
const { readFileSync } = await import('node:fs');
const { join } = await import('node:path');

const context = JSON.parse(
  readFileSync(join(process.cwd(), 'eval/cache/context.json'), 'utf8'),
);
const gold = loadAllGold();
const MODE = process.argv[2] || 'oracle';

const stats = { requests: 0, questions: 0, maxOptions: 0, maxStateChars: 0, maxQuestionChars: 0 };
let CURRENT = null;

const norm = (s) => (s == null ? null : String(s).toLowerCase());
const parseCfg = (v) => {
  if (!v) return null;
  try {
    return typeof v === 'object' ? v : JSON.parse(v);
  } catch {
    return null;
  }
};

/** Every (contentProperty -> schemaProperty) pair gold implies, including inner bindings. */
function goldBindings(g) {
  const out = [];
  for (const m of g.mappings) {
    if (!m.schemaProp) continue;
    if (m.contentProp) out.push({ content: m.contentProp, schema: m.schemaProp, m });
    const cfg = parseCfg(m.resolverConfig);
    for (const x of cfg?.complexTypeMappings || [])
      if (x.contentTypePropertyAlias) out.push({ content: x.contentTypePropertyAlias, schema: m.schemaProp, m });
  }
  return out;
}

function oracleAnswer(id, q) {
  const parts = id.split('__');
  const kind = parts[0];
  const g = CURRENT;
  const bySchema = (sp) => g.mappings.find((m) => norm(m.schemaProp) === norm(sp));

  if (kind === 'bind') {
    const alias = parts.slice(1).join('__');
    const hit = goldBindings(g).find((b) => norm(b.content) === norm(alias));
    const target = hit && q.criteria[hit.schema] ? hit.schema : '__none';
    return { type: 'choice', choice: target, confidence: target === '__none' ? 0.9 : 0.95, probabilities: {} };
  }

  if (kind === 'shape') {
    const cfg = parseCfg(bySchema(parts[1])?.resolverConfig);
    return {
      type: 'choice',
      choice: cfg?.extractAs === 'stringList' ? 'stringList' : 'nested',
      confidence: 0.95,
      probabilities: {},
    };
  }

  if (kind === 'entity')
    return { type: 'noul', noul: bySchema(parts[1])?.sourceType === 'complexType' ? 0.97 : 0.03 };

  // Descent: pick the option that is the gold nested type, or an ancestor of it.
  if (kind === 'root' || kind === 'desc') {
    const want = bySchema(parts[1])?.nestedType;
    const opts = Object.keys(q.criteria);
    if (!want) return { type: 'choice', choice: opts.includes('__stop') ? '__stop' : opts[0], confidence: 0.9, probabilities: {} };
    const exact = opts.find((o) => norm(o) === norm(want));
    if (exact) return { type: 'choice', choice: exact, confidence: 0.95, probabilities: {} };
    const onPath = opts.find((o) => o !== '__stop' && isSubtypeOf(want, o));
    if (onPath) return { type: 'choice', choice: onPath, confidence: 0.9, probabilities: {} };
    return { type: 'choice', choice: opts.includes('__stop') ? '__stop' : opts[0], confidence: 0.9, probabilities: {} };
  }

  if (kind === 'strlist') {
    const cfg = parseCfg(bySchema(parts[1])?.resolverConfig);
    const want = cfg?.contentProperty;
    return {
      type: 'choice',
      choice: want && q.criteria[want] ? want : Object.keys(q.criteria)[0],
      confidence: 0.95,
      probabilities: {},
    };
  }

  if (kind === 'inner') {
    const m = bySchema(parts[1]);
    const innerAlias = parts.slice(2).join('__');
    const cfg = parseCfg(m?.resolverConfig);
    const list = [...(cfg?.nestedMappings || []), ...(cfg?.complexTypeMappings || [])];
    const hit = list.find((x) => norm(x.contentProperty ?? x.contentTypePropertyAlias) === norm(innerAlias));
    const want = hit?.schemaProperty;
    return {
      type: 'choice',
      choice: want && q.criteria[want] ? want : '__none',
      confidence: 0.95,
      probabilities: {},
    };
  }

  return { type: 'choice', choice: '__none', confidence: 0.5, probabilities: {} };
}

globalThis.fetch = async (_url, init) => {
  const body = JSON.parse(init.body);
  stats.requests += 1;
  stats.questions += Object.keys(body.questions).length;
  stats.maxStateChars = Math.max(stats.maxStateChars, JSON.stringify(body.state).length);

  const answers = {};
  for (const [id, q] of Object.entries(body.questions)) {
    const optCount = q.criteria && !Array.isArray(q.criteria) ? Object.keys(q.criteria).length : 0;
    stats.maxOptions = Math.max(stats.maxOptions, optCount);
    stats.maxQuestionChars = Math.max(stats.maxQuestionChars, JSON.stringify(q).length);
    answers[id] =
      MODE === 'null'
        ? q.type === 'noul'
          ? { type: 'noul', noul: 0 }
          : { type: 'choice', choice: '__none', confidence: 0.1, probabilities: {} }
        : oracleAnswer(id, q);
  }
  return {
    ok: true,
    status: 200,
    json: async () => ({ model: 'dry-run', answers, usage: { input_tokens: 0, output_tokens: 0 } }),
    text: async () => '',
  };
};

const { typeSafeSuggest } = await import('./typesafe-mapper.mjs');

const per = [];
for (const s of SAMPLE) {
  const ctx = context[s.alias];
  const g = gold.get(s.alias.toLowerCase());
  if (!ctx || !g) continue;
  CURRENT = g;
  const { suggestions } = await typeSafeSuggest(ctx, {});
  const sc = scoreOne(g, suggestions);
  sc.tag = s.tag;
  per.push(sc);
  console.log(
    `  ${s.alias.padEnd(20)} [${s.tag}] strictF1=${sc.strict.f1} lenientF1=${sc.lenient.f1} ` +
      `rich=${sc.rich.hit}/${sc.rich.goldCount} cross=${sc.crossNode.hit}/${sc.crossNode.goldCount}` +
      (sc.rich.missed.length ? `\n      MISSED: ${sc.rich.missed.join(' | ')}` : ''),
  );
}

const agg = aggregate(per);
console.log(`\n=== ${MODE.toUpperCase()} ===`);
console.log(
  `rich ${agg.richCoverage.hit}/${agg.richCoverage.goldCount} (${Math.round(agg.richCoverage.pct * 100)}%)  ` +
    `strictF1=${agg.strictF1_macro}  lenientF1=${agg.lenientF1_macro}  ` +
    `cross ${agg.crossNodeCoverage.hit}/${agg.crossNodeCoverage.goldCount}`,
);
console.log(
  `requests=${stats.requests} questions=${stats.questions} maxOptions=${stats.maxOptions} ` +
    `maxStateChars=${stats.maxStateChars} maxQuestionChars=${stats.maxQuestionChars}`,
);
