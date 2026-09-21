// Self-test for the TypeSafe eval leg — no API key, no network.
//
// Stubs globalThis.fetch with a "gold oracle": a hypothetical perfect model that always
// picks the answer gold implies. The oracle's score is the CEILING of this harness — the
// best any model could score through it. A real run must be read against that ceiling,
// not against 1.0, and if the ceiling drops, the harness (question design, assembly or
// scoring integration) has broken rather than the model.
//
// Current ceiling: rich 11/12, strict F1 0.906. That is the v1 figure and it stays the
// printed figure on purpose: eval/typesafe-mapper.mjs is the FROZEN v1 specification, so
// the shortfall below is v1's scope, not a harness fault:
//   - nested-block `routes` (nestedBlocksPage.hasPart) is not attempted
//   - cross-node sources (ancestor/parent/sibling, 6 rows) are not attempted: the cached
//     context carries no information about surrounding content types
//   - gold maps a few single content properties to TWO schema properties (newsArticle
//     title -> Headline AND Name); the content->schema binding direction emits one
//
// v2 (cross-node sources, per-block routes, multiple targets, value schema) lives in the C#
// package and is measured LIVE against the TestHost by eval/run-typesafe-live.mjs. The
// oracle below already answers the v2 question kinds the C# (and any future JS port) asks:
// cross__<schemaProp> with sourceType__typeAlias__propertyAlias option keys, and the
// route-path forms of root__/desc__/inner__/shape__/strlist__ (a path being
// <schemaProp>__<blockAlias>[__<field>__<blockAlias>]*), with rroot__/rinner__/rshape__/
// rstrlist__ accepted as aliases. So the same gold-implies-the-answer stub can drive the C#
// unit tests' ceiling; the JS mapper simply never asks them, which is why the printed
// ceiling does not move.
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
const { pathToFileURL } = await import('node:url');

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

// ---------------------------------------------------------------------------
// v2 gold lookups: routes and cross-node rows
// ---------------------------------------------------------------------------

const CROSS_RELATIONS = new Set(['parent', 'ancestor', 'sibling']);
const NUMERIC = /^\d+$/;

/** Tokens of an id or option key: split on the `__` joiner and anything JudgmentSession.Id sanitises. */
const tokens = (s) => String(s).split(/__|[^A-Za-z0-9_]+/).filter(Boolean);

/**
 * Every gold route, flattened with the chain of ids that reaches it:
 * [schemaProp, blockAlias, (contentProperty, blockAlias)*]. The C# builds a route question
 * id from that chain (the block alias path), so a question path is matched as an in-order
 * subsequence of a chain whose first and last elements agree.
 */
function goldRoutes(g) {
  const out = [];
  const walk = (routes, chain, depth) => {
    for (const r of routes || []) {
      const here = [...chain, r.blockAlias];
      out.push({ chain: here, route: r, depth });
      for (const pm of r.propertyMappings || []) {
        if (pm.routes) walk(pm.routes, [...here, pm.contentProperty], depth + 1);
      }
    }
  };
  for (const m of g.mappings) {
    const cfg = parseCfg(m.resolverConfig);
    if (m.schemaProp && cfg?.routes) walk(cfg.routes, [m.schemaProp], 1);
  }
  return out;
}

function isSubsequence(path, chain) {
  let i = 0;
  for (const c of chain) if (i < path.length && norm(c) === norm(path[i])) i++;
  return i === path.length;
}

/** The gold route a question path names, deepest match first, or null when gold has none there. */
function routeByPath(g, path) {
  if (path.length === 0) return null;
  const hits = goldRoutes(g).filter(
    (e) =>
      norm(e.chain[0]) === norm(path[0]) &&
      norm(e.chain[e.chain.length - 1]) === norm(path[path.length - 1]) &&
      isSubsequence(path, e.chain),
  );
  return hits.sort((a, b) => b.depth - a.depth)[0]?.route ?? null;
}

/** The gold route binding for a block field: the route by path, then its propertyMapping for that field. */
function routeBinding(g, path, fieldAlias) {
  const r = routeByPath(g, path);
  return r?.propertyMappings?.find((pm) => norm(pm.contentProperty) === norm(fieldAlias)) ?? null;
}

/**
 * The cross-node option whose decoded relation / source type / property alias matches the
 * gold row. The C# encodes an option from the neighbourhood (relation, type alias, property
 * alias) through JudgmentSession.Id, so the decode is tolerant: an option matches when its
 * tokens carry the gold property alias and the gold source content type, and name no
 * relation other than the gold one.
 */
function crossOption(q, want) {
  if (!want) return null;
  const relations = [...CROSS_RELATIONS];
  return Object.keys(q.criteria).find((opt) => {
    if (opt.startsWith('__')) return false;
    const t = tokens(opt).map(norm);
    if (!t.includes(norm(want.contentProp))) return false;
    if (want.sourceContentType && !t.includes(norm(want.sourceContentType))) return false;
    const named = relations.filter((r) => t.includes(r));
    return named.length === 0 || (named.length === 1 && named[0] === norm(want.sourceType));
  });
}

/** Test hook: point the oracle at one gold mapping (the scorer self-test and a C# port of the stub use it). */
export function setGold(g) {
  CURRENT = g;
}

export function oracleAnswer(id, q) {
  const parts = id.split('__');
  const kind = parts[0];
  const g = CURRENT;
  const bySchema = (sp) => g.mappings.find((m) => norm(m.schemaProp) === norm(sp));
  const pick = (choice, confidence = 0.95) => ({ type: 'choice', choice, confidence, probabilities: {} });

  // ---- v2: cross-node sources ----
  // cross__<schemaProp>: the option encoding gold's relation/type/alias for that schema
  // property, else __none (gold has no cross-node row there, or the option was not offered).
  if (kind === 'cross') {
    const want = g.mappings.find(
      (m) => norm(m.schemaProp) === norm(parts.slice(1).join('__')) && CROSS_RELATIONS.has(m.sourceType),
    );
    const opt = crossOption(q, want);
    return pick(opt ?? '__none', opt ? 0.95 : 0.9);
  }

  // ---- v2: per-block routes ----
  // The C# BlockRoutePlanner reuses the v1 prefixes with a route PATH in place of the bare
  // schema property (root__<path>, desc__<path>__<depth>__<i>, inner__<path>__<field>,
  // shape__<path>__<field>, strlist__<path>__<field>, where a path is
  // <schemaProp>__<blockAlias>[__<field>__<blockAlias>]*). The r-prefixed spellings below are
  // accepted as aliases for a port that wants to keep the rounds distinguishable. Each v1
  // handler therefore tries the v1 lookup first (a legacy nestedMappings gold row answers a
  // route question about the same list, e.g. faqPage mainEntity/faqItem) and then the route.
  const last = parts[parts.length - 1];
  const routePath = parts.slice(1, -1);

  if (kind === 'bind') {
    const alias = parts.slice(1).join('__');
    const hit = goldBindings(g).find((b) => norm(b.content) === norm(alias));
    const target = hit && q.criteria[hit.schema] ? hit.schema : '__none';
    return pick(target, target === '__none' ? 0.9 : 0.95);
  }

  // shape__<schemaProp> (v1) or shape__<path>__<field> (a nested list field inside a block):
  // stringList when the gold config says so, otherwise nested (the v1 default).
  if (kind === 'shape' || kind === 'rshape') {
    const cfg = parseCfg(bySchema(parts[1])?.resolverConfig);
    const binding = parts.length > 2 ? routeBinding(g, routePath, last) : null;
    const extractAs = binding ? binding.extractAs : cfg?.extractAs;
    return pick(extractAs === 'stringList' ? 'stringList' : 'nested');
  }

  if (kind === 'entity')
    return { type: 'noul', noul: bySchema(parts[1])?.sourceType === 'complexType' ? 0.97 : 0.03 };

  // Descent: pick the option that is the gold nested type, or an ancestor of it. A route
  // subject's key is its path, so when the mapping-level nested type is absent the gold
  // route's type is used instead (depth and beam index are the trailing numbers). A route
  // root question also offers __none ("this block type does not belong here"), which is
  // the answer when gold has neither a nested type nor a route for that block.
  if (kind === 'root' || kind === 'desc' || kind === 'rroot') {
    const subject = parts.slice(1).filter((p) => !NUMERIC.test(p));
    const want = bySchema(parts[1])?.nestedType ?? routeByPath(g, subject)?.nestedSchemaType;
    const opts = Object.keys(q.criteria);
    const bail = () => pick(opts.includes('__none') ? '__none' : opts.includes('__stop') ? '__stop' : opts[0], 0.9);
    if (!want) return bail();
    const exact = opts.find((o) => norm(o) === norm(want));
    if (exact) return pick(exact);
    // Nearest ancestor of the gold type when several are offered (CreativeWork over Thing).
    const onPath = opts
      .filter((o) => !o.startsWith('__') && isSubtypeOf(want, o))
      .sort((a, b) => (isSubtypeOf(a, b) ? -1 : isSubtypeOf(b, a) ? 1 : 0))[0];
    return onPath ? pick(onPath, 0.9) : bail();
  }

  // strlist__<schemaProp> (v1) or strlist__<path>__<field>: the inner field the string list reads.
  if (kind === 'strlist' || kind === 'rstrlist') {
    const cfg = parseCfg(bySchema(parts[1])?.resolverConfig);
    const binding = parts.length > 2 ? routeBinding(g, routePath, last) : null;
    const want = binding ? (binding.nestedContentProperty ?? binding.contentProperty) : cfg?.contentProperty;
    return pick(want && q.criteria[want] ? want : Object.keys(q.criteria)[0]);
  }

  // inner__<schemaProp>__<field> (v1) or inner__<path>__<field>: the gold binding's schema
  // property for that field, from the mapping-level config first, then the gold route.
  if (kind === 'inner' || kind === 'rinner') {
    const cfg = parseCfg(bySchema(parts[1])?.resolverConfig);
    const list = [...(cfg?.nestedMappings || []), ...(cfg?.complexTypeMappings || [])];
    const aliases = [parts.slice(2).join('__'), last];
    const hit =
      list.find((x) => aliases.some((a) => norm(x.contentProperty ?? x.contentTypePropertyAlias) === norm(a))) ??
      routeBinding(g, routePath, last);
    const want = hit?.schemaProperty;
    return pick(want && q.criteria[want] ? want : '__none');
  }

  return pick('__none', 0.5);
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

// Direct execution only: importing the module installs the fetch stub and the exports
// without running the whole sample.
if (import.meta.url === pathToFileURL(process.argv[1]).href) await main();

async function main() {
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
}
