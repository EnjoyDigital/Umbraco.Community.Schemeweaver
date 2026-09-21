// The TypeSafe leg of the eval harness: produce a property mapping for one content
// type out of small typed judgments instead of one free-form LLM completion.
//
// SHAPE OF THE APPROACH
// ---------------------
// Code owns the workflow, the rules and the vocabulary; Jev only supplies the semantic
// judgments a name-matching heuristic cannot make. Concretely:
//
//   Round 1  bind    — for EACH content property, "which property of <SchemaType> does
//                      this supply?" as a Choice over that schema type's real property
//                      names plus __none. The model selects from a closed set, so it
//                      cannot invent an alias or a schema property. This is the
//                      select-don't-generate pattern from the value-extraction cookbook,
//                      and it structurally removes the failure mode the Anthropic
//                      satellite needs a JSON-salvage routine to survive.
//
//   Round 2  shape   — for the bound rows only, decide the SOURCE TYPE: a Noul for
//                      "is this a distinct named entity (nest it) or a plain value?",
//                      and, for block lists, whether the blocks flatten to strings or
//                      become nested objects. Needs round 1, so it is a second request.
//
//   Round 2b descent — pick the nested Schema.org type by walking DOWN the type tree
//                      from the property's declared range, one Choice per level with a
//                      "stop here" option. Hierarchical classification: it keeps every
//                      option list small, and — because the walk starts at the declared
//                      range — an out-of-range nested type is not expressible. Levels
//                      are batched across all rows, so depth costs requests, not rows.
//
//   Round 3  inner   — bind the inner properties of the rich rows: each block field (or
//                      each content property that claimed a complex schema property)
//                      chooses a property of the nested type.
//
// Everything else is code: which source types are structurally possible, the declared
// range, media properties never becoming complexType shells, collision resolution, and
// the assembly of resolverConfig JSON.

import { askChunked, choice, noul } from './typesafe.mjs';
import { TYPES, childrenOf, propertiesOf, propertyOf, rangeOf } from './schema-graph.mjs';

const BLOCK_EDITORS = new Set(['Umbraco.BlockList', 'Umbraco.BlockGrid']);
const MEDIA_EDITORS = new Set([
  'Umbraco.MediaPicker3',
  'Umbraco.MediaPicker',
  'Umbraco.ImageCropper',
  'Umbraco.UploadField',
]);

const NONE = '__none';
const STOP = '__stop';

/** A Choice question allows at most 255 options; one slot is reserved for __none. */
const MAX_OPTIONS = 254;

/** How many ranked schema properties to offer as binding targets. */
export const DEFAULT_TARGET_LIMIT = MAX_OPTIONS;

/** Maximum levels the nested-type descent will walk down the Schema.org tree. */
const MAX_DESCENT_DEPTH = 5;

const qid = (...parts) => parts.join('__').replace(/[^A-Za-z0-9_]/g, '_');

/** Human-readable option label for a schema property: its name plus what it accepts. */
function describeSchemaProp(p) {
  const accepts = (p.acceptedTypes || []).join(' or ');
  return accepts ? `${p.name} — accepts ${accepts}` : p.name;
}

/**
 * Describe a content property for the shared state. A Block List is described BY ITS
 * CONTENTS: without the element types and their fields, `faqItems (Umbraco.BlockList)`
 * is an opaque name and the binding judgment has nothing to reason about, which is
 * exactly how a body-sections container ends up unmapped.
 */
function describeContentProp(p, ctx) {
  const base = {
    alias: p.alias,
    name: p.name,
    editor: p.editorAlias,
    ...(p.description ? { description: p.description } : {}),
  };
  if (!BLOCK_EDITORS.has(p.editorAlias)) return base;

  const blocks = blocksFor(ctx, p.alias).map((el) => ({
    blockType: el.alias,
    blockName: el.name,
    fields: (el.propertyInfos || []).map((ip) => ({ alias: ip.alias, name: ip.name, editor: ip.editorAlias })),
  }));
  return { ...base, isRepeatingList: true, blockTypes: blocks };
}

/** One-line summary of a block property's contents, for the question text. */
function summariseBlocks(ctx, alias) {
  const els = blocksFor(ctx, alias);
  if (els.length === 0) return '';
  const parts = els.map(
    (el) => `"${el.name || el.alias}" (fields: ${(el.propertyInfos || []).map((ip) => ip.alias).join(', ')})`,
  );
  return ` It is a repeating list; each entry is one of these block types: ${parts.join('; ')}.`;
}

/**
 * Binding targets for a content type: the schema type's ranked properties, capped at
 * the Choice limit. The ranking is SchemeWeaver's own (cached with the context) and is
 * used only to decide what to drop if a type somehow exceeds 254 properties — not to
 * pre-filter plausible targets, which would cap recall.
 */
function targetCriteria(ctx, limit) {
  const ranked = (ctx.rankedSchemaProperties || []).slice(0, Math.min(limit, MAX_OPTIONS));
  const criteria = {};
  for (const p of ranked) criteria[p.name] = describeSchemaProp(p);
  criteria[NONE] = 'None of these — this content property has no good Schema.org target here.';
  return criteria;
}

function blocksFor(ctx, contentAlias) {
  const b = ctx.blockElementTypes?.[contentAlias];
  return Array.isArray(b) ? b : [];
}

/** All inner property records across a block property's element types, de-duplicated. */
function blockInnerProps(ctx, contentAlias) {
  const out = new Map();
  for (const el of blocksFor(ctx, contentAlias)) {
    for (const ip of el.propertyInfos || []) {
      if (!out.has(ip.alias)) out.set(ip.alias, { ...ip, blockAlias: el.alias, blockName: el.name });
    }
  }
  return [...out.values()];
}

// ---------------------------------------------------------------------------
// Round 1 — bind each content property to a schema property (or none)
// ---------------------------------------------------------------------------

function buildBindState(ctx) {
  return {
    umbracoContentType: {
      alias: ctx.alias,
      properties: (ctx.contentProperties || []).map((p) => describeContentProp(p, ctx)),
    },
    targetSchemaType: ctx.schemaType,
    note:
      'Built-in properties are prefixed with __: __name is the node name, __url its URL, ' +
      '__createDate and __updateDate its timestamps.',
  };
}

function buildBindQuestions(ctx, limit) {
  const criteria = targetCriteria(ctx, limit);
  const questions = {};
  for (const p of ctx.contentProperties || []) {
    const isBlock = BLOCK_EDITORS.has(p.editorAlias);
    questions[qid('bind', p.alias)] = choice(
      `The Umbraco content property \`${p.alias}\` (label "${p.name}", editor ${p.editorAlias}) ` +
        `holds part of this page's content.${isBlock ? summariseBlocks(ctx, p.alias) : ''} ` +
        `Which single property of the Schema.org type ${ctx.schemaType} should be populated from it?` +
        (isBlock
          ? ` A repeating list of content like this is the page's structured body — it belongs on a ` +
            `property that holds a collection of things (the page's main entity, its parts, its list ` +
            `items), not left out. Judge it by what the blocks actually contain.`
          : '') +
        ` Choose ${NONE} if none of the listed properties is a good semantic fit for what this ` +
        `property actually holds.`,
      criteria,
    );
  }
  return questions;
}

// ---------------------------------------------------------------------------
// Round 2 — source type / shape
// ---------------------------------------------------------------------------

function buildShapeQuestions(rows, ctx) {
  const questions = {};
  for (const r of rows) {
    if (r.isBlock) {
      if (r.innerProps.length > 1) {
        questions[qid('shape', r.schemaProp)] = choice(
          `The Umbraco Block List property \`${r.contentProp}\` supplies ${ctx.schemaType}.${r.schemaProp}. ` +
            `Each block holds these fields: ${r.innerProps.map((p) => `\`${p.alias}\``).join(', ')}. ` +
            `Should each block become one nested Schema.org object carrying several of those fields, ` +
            `or should the blocks flatten to a plain list of text values?`,
          {
            nested:
              'Each block is a distinct thing with several meaningful fields (a step, a question and its answer, a team member).',
            stringList:
              'Each block carries essentially one meaningful label, so the list is just a list of strings (ingredients, tools, tags).',
          },
        );
      }
      continue;
    }

    if (r.canBeComplex) {
      // The entity-vs-value judgment: the single most common way the heuristic and a
      // careless LLM both go wrong (wrapping a lone scalar in a pointless object shell).
      const claimants = r.claimants.map((c) => `\`${c.contentProp}\``).join(', ');
      questions[qid('entity', r.schemaProp)] = noul(
        `The Umbraco ${r.claimants.length > 1 ? 'properties' : 'property'} ${claimants} ` +
          `supply ${ctx.schemaType}.${r.schemaProp}. Does ${r.schemaProp} denote a distinct named ` +
          `entity — a person, organisation, place or similar thing that deserves its own nested object ` +
          `with its own properties — rather than a plain value that should be written straight out?`,
        {
          true: `${r.schemaProp} names a thing in its own right, so it should be emitted as a nested Schema.org object with its own properties.`,
          false: `${r.schemaProp} is a plain value (text, a number, a date, a URL, an image) and should be written straight out as a scalar.`,
        },
      );
    }
  }
  return questions;
}

// ---------------------------------------------------------------------------
// Round 2b — nested type descent (hierarchical classification)
// ---------------------------------------------------------------------------

/** Beam width for the nested-type descent. */
const BEAM_WIDTH = 3;

/**
 * Walk down the Schema.org tree to pick each rich row's nested type, one level at a
 * time, batching every row's questions for a level into one request. Starts from the
 * property's declared range, so the result is always within range by construction.
 *
 * BEAM SEARCH, not greedy. A greedy walk commits to the highest-probability child at
 * every level and cannot recover from an early wrong turn — measured here as exactly
 * the instability the hierarchical-classification cookbook describes: tightening the
 * stop rule fixed Product.review (which had over-specified to UserReview) and broke
 * Recipe.recipeInstructions (which then stopped short of HowToStep). Keeping the best
 * BEAM_WIDTH paths alive and ranking them at the end by geometric-mean edge probability
 * lets deeper evidence repair an ambiguous early decision, and compares shallow and deep
 * landing points fairly.
 */
async function descendNestedTypes(rows, ctx, state) {
  const active = rows
    .filter((r) => r.needsNestedType)
    .map((r) => {
      const roots = rangeOf(ctx.schemaType, r.schemaProp);
      return { row: r, roots, current: null, done: false };
    })
    .filter((d) => d.roots.length > 0);

  if (active.length === 0) return;

  // Level 0: which declared range root, when a property accepts more than one.
  const rootQuestions = {};
  for (const d of active) {
    if (d.roots.length === 1) {
      d.current = d.roots[0];
      continue;
    }
    rootQuestions[qid('root', d.row.schemaProp)] = choice(
      `${describeSubject(d.row, ctx)} Which of these Schema.org types is the right family for it?`,
      Object.fromEntries(d.roots.map((t) => [t, `It is a ${t} (or a more specific kind of ${t}).`])),
    );
  }
  if (Object.keys(rootQuestions).length) {
    const answers = await askChunked(state, rootQuestions, 12);
    for (const d of active) {
      if (d.current) continue;
      const a = answers[qid('root', d.row.schemaProp)];
      d.current = a?.choice && TYPES[a.choice] ? a.choice : d.roots[0];
    }
  }

  // Each row starts with one beam sitting on its chosen range root.
  for (const d of active) d.beams = [{ type: d.current, logp: 0, steps: 0, done: false }];

  // Levels 1..N: each live beam narrows to a subtype, or stops at its current type.
  for (let depth = 0; depth < MAX_DESCENT_DEPTH; depth++) {
    const questions = {};
    const asked = [];

    for (const d of active) {
      d.beams.forEach((b, i) => {
        if (b.done) return;
        const kids = childrenOf(b.type).filter((k) => TYPES[k]);
        if (kids.length === 0) {
          b.done = true;
          return;
        }
        const id = qid('desc', d.row.schemaProp, String(depth), String(i));
        asked.push({ d, b, id });
        // `__stop` is listed first and framed as the default: search engines key their
        // rich results off the common Schema.org types (Review, Offer, Person, Place),
        // so narrowing to an exotic subtype is a real-world regression, not precision.
        questions[id] = choice(
          `${describeSubject(d.row, ctx)} It is a ${b.type}, and ${b.type} is an acceptable, ` +
            `widely-understood answer. Only choose a more specific subtype if the content clearly ` +
            `shows it is that narrower kind of thing.`,
          {
            [STOP]: `Stay with ${b.type} — nothing in the content positively demands a narrower type.`,
            ...Object.fromEntries(
              kids
                .slice(0, MAX_OPTIONS)
                .map((t) => [t, `The content shows it is specifically a ${t}, not just any ${b.type}.`]),
            ),
          },
        );
      });
    }

    if (asked.length === 0) break;
    const answers = await askChunked(state, questions, 12);

    for (const d of active) {
      const next = [];
      for (const b of d.beams) {
        if (b.done) {
          next.push(b);
          continue;
        }
        const entry = asked.find((x) => x.b === b && x.d === d);
        const a = entry ? answers[entry.id] : null;
        if (!a) {
          next.push({ ...b, done: true });
          continue;
        }
        const probs = a.probabilities && Object.keys(a.probabilities).length
          ? a.probabilities
          : { [a.choice]: a.confidence ?? 1 };

        // Expand every option worth following; the beam cut below does the pruning.
        for (const [option, p] of Object.entries(probs)) {
          if (!(p > 0)) continue;
          if (option === STOP) {
            next.push({ ...b, logp: b.logp + Math.log(p), steps: b.steps + 1, done: true });
          } else if (TYPES[option]) {
            next.push({ type: option, logp: b.logp + Math.log(p), steps: b.steps + 1, done: false });
          }
        }
      }
      // Rank by geometric-mean edge probability so a shallow landing point and a deep
      // one are compared fairly, then keep the best BEAM_WIDTH.
      d.beams = next
        .sort((x, y) => geoMean(y) - geoMean(x))
        .slice(0, BEAM_WIDTH);
    }

    if (active.every((d) => d.beams.every((b) => b.done))) break;
  }

  for (const d of active) {
    const winner = d.beams.slice().sort((x, y) => geoMean(y) - geoMean(x))[0];
    d.row.nestedType = winner?.type ?? d.current;
    d.row.descentConfidence = winner ? geoMean(winner) : 1;
  }
}

/** Geometric-mean edge probability of a beam: comparable across path lengths. */
const geoMean = (b) => (b.steps === 0 ? 1 : Math.exp(b.logp / b.steps));

/** A one-line description of what a rich row's nested objects actually represent. */
function describeSubject(row, ctx) {
  if (row.isBlock) {
    return (
      `Each block of the Umbraco Block List \`${row.contentProp}\` (block type "${row.innerProps[0]?.blockName ?? row.contentProp}", ` +
      `fields: ${row.innerProps.map((p) => p.alias).join(', ')}) will be emitted as a nested Schema.org ` +
      `object under ${ctx.schemaType}.${row.schemaProp}.`
    );
  }
  const claimants = row.claimants.map((c) => `\`${c.contentProp}\` (label "${c.contentName}")`).join(', ');
  return (
    `The Umbraco ${row.claimants.length > 1 ? 'properties' : 'property'} ${claimants} will be assembled ` +
    `into one nested Schema.org object under ${ctx.schemaType}.${row.schemaProp}.`
  );
}

// ---------------------------------------------------------------------------
// Round 3 — inner bindings
// ---------------------------------------------------------------------------

function nestedCriteria(nestedType) {
  const criteria = {};
  for (const p of propertiesOf(nestedType).slice(0, MAX_OPTIONS)) criteria[p.name] = describeSchemaProp(p);
  criteria[NONE] = `None of these — this field has no good ${nestedType} property.`;
  return criteria;
}

function buildInnerQuestions(rows, ctx) {
  const questions = {};
  for (const r of rows) {
    if (r.sourceType === 'blockContent' && r.shape === 'nested' && r.nestedType) {
      for (const ip of r.innerProps) {
        questions[qid('inner', r.schemaProp, ip.alias)] = choice(
          `Inside the block \`${ip.blockAlias}\`, the field \`${ip.alias}\` (label "${ip.name}", ` +
            `editor ${ip.editorAlias}) holds content. Each block is emitted as a Schema.org ` +
            `${r.nestedType}. Which property of ${r.nestedType} should this field populate?`,
          nestedCriteria(r.nestedType),
        );
      }
    }

    if (r.sourceType === 'blockContent' && r.shape === 'stringList' && r.innerProps.length > 1) {
      questions[qid('strlist', r.schemaProp)] = choice(
        `The blocks in \`${r.contentProp}\` flatten to a plain list of text values for ` +
          `${ctx.schemaType}.${r.schemaProp}. Which single field of the block carries the text ` +
          `that should appear in that list?`,
        Object.fromEntries(
          r.innerProps.map((p) => [p.alias, `The \`${p.alias}\` field (label "${p.name}", ${p.editorAlias}).`]),
        ),
      );
    }

    if (r.sourceType === 'complexType' && r.nestedType) {
      for (const c of r.claimants) {
        questions[qid('inner', r.schemaProp, c.contentProp)] = choice(
          `${ctx.schemaType}.${r.schemaProp} is emitted as a nested ${r.nestedType}. The Umbraco ` +
            `property \`${c.contentProp}\` (label "${c.contentName}", editor ${c.editorAlias}) is one of ` +
            `its source fields. Which property of ${r.nestedType} should that value populate?`,
          nestedCriteria(r.nestedType),
        );
      }
    }
  }
  return questions;
}

/**
 * The property a wrapped value lands on inside a wrapper entity. Text-bearing wrappers
 * (the comment family) carry it on Text; everything else carries a Name. A rule, from
 * how Schema.org models those types.
 */
function wrapProperty(wrapType) {
  return ['Answer', 'Comment', 'Question'].includes(wrapType) ? 'Text' : 'Name';
}

// ---------------------------------------------------------------------------
// Orchestration
// ---------------------------------------------------------------------------

export async function typeSafeSuggest(ctx, { targetLimit = DEFAULT_TARGET_LIMIT, minConfidence = 0 } = {}) {
  const trace = { bind: {}, shape: {}, inner: {} };
  const state = buildBindState(ctx);

  // --- Round 1: bind -------------------------------------------------------
  const bindAnswers = await askChunked(state, buildBindQuestions(ctx, targetLimit), 12);
  trace.bind = bindAnswers;

  /** schemaProp(lower) -> { schemaProp, claimants[] } */
  const claims = new Map();
  for (const p of ctx.contentProperties || []) {
    const a = bindAnswers[qid('bind', p.alias)];
    if (!a || a.choice === NONE || a.choice == null) continue;
    const conf = a.confidence ?? 0;
    if (conf < minConfidence) continue;

    const key = String(a.choice).toLowerCase();
    if (!claims.has(key)) claims.set(key, { schemaProp: a.choice, claimants: [] });
    claims.get(key).claimants.push({
      contentProp: p.alias,
      contentName: p.name,
      editorAlias: p.editorAlias,
      confidence: conf,
    });
  }

  const rows = [];
  for (const { schemaProp: sp, claimants } of claims.values()) {
    claimants.sort((a, b) => b.confidence - a.confidence);
    const reg = propertyOf(ctx.schemaType, sp);
    const primary = claimants[0];
    const isBlock = BLOCK_EDITORS.has(primary.editorAlias);
    const isMedia = MEDIA_EDITORS.has(primary.editorAlias);
    const innerProps = isBlock ? blockInnerProps(ctx, primary.contentProp) : [];

    // A media property resolves to a fully-populated ImageObject through the media
    // resolver, so it must stay `property` — wrapping it produces an empty shell. That
    // trap is documented on SchemaAutoMapper's *.logo popular default.
    const canBeComplex = !isBlock && !isMedia && !!reg?.isComplexType && rangeOf(ctx.schemaType, sp).length > 0;

    rows.push({
      schemaProp: sp,
      contentProp: primary.contentProp,
      contentName: primary.contentName,
      editorAlias: primary.editorAlias,
      confidence: primary.confidence,
      // Several content properties can legitimately assemble ONE nested entity
      // (locationName + locationAddress -> Place). For a scalar target only the most
      // confident claim survives; for a nested entity they are all source fields.
      claimants: canBeComplex || isBlock ? claimants : [primary],
      isBlock,
      isMedia,
      innerProps,
      canBeComplex,
      sourceType: isBlock ? 'blockContent' : 'property',
      shape: null,
      nestedType: null,
      needsNestedType: false,
      descentConfidence: 1,
    });
  }

  // --- Round 2: shape ------------------------------------------------------
  const shapeQuestions = buildShapeQuestions(rows, ctx);
  const shapeAnswers = Object.keys(shapeQuestions).length ? await askChunked(state, shapeQuestions, 12) : {};
  trace.shape = shapeAnswers;

  for (const r of rows) {
    if (r.isBlock) {
      const a = shapeAnswers[qid('shape', r.schemaProp)];
      r.shape = r.innerProps.length <= 1 ? 'stringList' : a?.choice === 'stringList' ? 'stringList' : 'nested';
      r.needsNestedType = r.shape === 'nested';
      continue;
    }
    if (r.canBeComplex) {
      const a = shapeAnswers[qid('entity', r.schemaProp)];
      if ((a?.noul ?? 0) >= 0.5) {
        r.sourceType = 'complexType';
        r.needsNestedType = true;
      }
    }
  }

  // --- Round 2b: nested type descent --------------------------------------
  await descendNestedTypes(rows, ctx, state);
  for (const r of rows) {
    if (r.needsNestedType && !r.nestedType) {
      // No usable range: fall back to the shape that needs no nested type.
      if (r.isBlock) r.shape = 'stringList';
      else r.sourceType = 'property';
    }
  }

  // --- Round 3: inner bindings --------------------------------------------
  const innerQuestions = buildInnerQuestions(rows, ctx);
  const innerAnswers = Object.keys(innerQuestions).length ? await askChunked(state, innerQuestions, 12) : {};
  trace.inner = innerAnswers;

  // --- Assemble ------------------------------------------------------------
  const suggestions = [];
  for (const r of rows) {
    const base = {
      schemaPropertyName: r.schemaProp,
      suggestedContentTypePropertyAlias: r.contentProp,
      suggestedSourceType: r.sourceType,
      suggestedNestedSchemaTypeName: r.nestedType,
      suggestedResolverConfig: null,
      confidence: Math.round(r.confidence * 100),
    };

    if (r.sourceType === 'blockContent' && r.shape === 'stringList') {
      const pick =
        r.innerProps.length === 1
          ? r.innerProps[0].alias
          : (innerAnswers[qid('strlist', r.schemaProp)]?.choice ?? r.innerProps[0]?.alias);
      if (!pick) continue;
      base.suggestedNestedSchemaTypeName = null;
      base.suggestedResolverConfig = JSON.stringify({ extractAs: 'stringList', contentProperty: pick });
    }

    if (r.sourceType === 'blockContent' && r.shape === 'nested' && r.nestedType) {
      // NOT de-duplicated by schema property: a Block List usually allows several
      // element types, and two of them can legitimately feed the same nested property
      // (heroBlock.heroImage and featureBlock.featureImage both supply Image). Dropping
      // the second would silently lose one block type's content.
      const nestedMappings = [];
      for (const ip of r.innerProps) {
        const a = innerAnswers[qid('inner', r.schemaProp, ip.alias)];
        if (!a || a.choice === NONE || a.choice == null) continue;

        // If the chosen nested property is itself an entity, the value has to be wrapped
        // (Question.acceptedAnswer -> Answer.text). Which property to wrap into is a RULE
        // taken from the registry's declared range for that nested property.
        const target = propertyOf(r.nestedType, a.choice);
        const wrapType = target?.isComplexType
          ? (target.acceptedTypes || []).find((t) => TYPES[t])
          : null;
        nestedMappings.push({
          schemaProperty: a.choice,
          contentProperty: ip.alias,
          ...(wrapType ? { wrapInType: wrapType, wrapInProperty: wrapProperty(wrapType) } : {}),
        });
      }
      if (nestedMappings.length === 0) continue;
      base.suggestedResolverConfig = JSON.stringify({ nestedMappings });
    }

    if (r.sourceType === 'complexType' && r.nestedType) {
      const complexTypeMappings = [];
      const used = new Set();
      for (const c of r.claimants) {
        const a = innerAnswers[qid('inner', r.schemaProp, c.contentProp)];
        const target = a && a.choice !== NONE && a.choice != null ? a.choice : null;
        if (!target || used.has(target)) continue;
        used.add(target);
        complexTypeMappings.push({
          schemaProperty: target,
          sourceType: 'property',
          contentTypePropertyAlias: c.contentProp,
        });
      }
      if (complexTypeMappings.length === 0) continue;
      base.suggestedResolverConfig = JSON.stringify({ complexTypeMappings });
    }

    suggestions.push(base);
  }

  return { suggestions, trace };
}
