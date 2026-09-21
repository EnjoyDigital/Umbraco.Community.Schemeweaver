// Scoring for the SchemeWeaver eval harness.
//
// Compares a candidate's property-mapping suggestions against the gold mapping for a
// content type. Produces:
//   - lenient F1  : schemaProp + target contentProp match (ignores sourceType) — secondary guard
//   - strict  F1  : schemaProp + contentProp + sourceType (+ nestedType for rich) match
//   - rich        : of the gold's self-contained rich mappings, how many were reproduced
//                   at STRICT level — THE PRIMARY METRIC (where AI must beat the heuristic)
//   - crossNode   : same idea for ancestor/sibling/parent (stretch). A strict cross-node
//                   match also requires the SOURCE CONTENT TYPE (v2): at runtime an ancestor
//                   or sibling row reads the nearest node OF THAT TYPE, so a row naming the
//                   wrong type reads the wrong node, not just the wrong string.
//
// blockContent gold rows whose resolver carries per-block `routes` (blocks inside blocks,
// Block Grid areas, one type per block alias) are compared route by route, recursively
// (see routesCover). A candidate with no routes never satisfies a routes gold row; a
// candidate that emits routes CAN satisfy a legacy nestedMappings gold row when every route
// lands on the gold nested type and the union of its bindings covers the gold bindings.

const norm = (s) => (s == null ? null : String(s).toLowerCase());

function parseResolver(v) {
  if (v == null) return null;
  if (typeof v === 'object') return v;
  try {
    return JSON.parse(v);
  } catch {
    return null;
  }
}

// Inner-binding extractors. The load-bearing part of a rich mapping is the resolverConfig:
// for complexType the {schemaProperty -> contentProperty} sub-mappings; for blockContent
// either an extractAs:stringList(contentProperty) or nestedMappings. The parent contentProp is
// a DEAD field for complexType at runtime (JsonLdGenerator only reads NestedSchemaTypeName +
// ResolverConfig), so we verify the inner config instead of the parent alias.
function bindings(resolver, key) {
  const arr = resolver?.[key];
  if (!Array.isArray(arr)) return {};
  const out = {};
  for (const m of arr) {
    const sp = norm(m.schemaProperty ?? m.SchemaProperty);
    const cp = norm(m.contentProperty ?? m.contentTypePropertyAlias ?? m.ContentProperty);
    if (sp) out[sp] = cp;
  }
  return out;
}
function stringListProp(resolver) {
  const ea = norm(resolver?.extractAs);
  return ea === 'stringlist' ? norm(resolver?.contentProperty) : null;
}
/** Every gold binding must be reproduced by the candidate (extra candidate bindings allowed). */
function coversBindings(aiMap, goldMap) {
  const keys = Object.keys(goldMap);
  if (keys.length === 0) return true;
  return keys.every((k) => aiMap[k] === goldMap[k]);
}

/** Normalise a candidate suggestion (AI or heuristic shape) into a common record. */
export function normaliseSuggestion(s) {
  return {
    schemaProp: norm(s.schemaPropertyName ?? s.SchemaPropertyName),
    contentProp: norm(
      s.suggestedContentTypePropertyAlias ??
        s.contentTypePropertyAlias ??
        s.ContentTypePropertyAlias ??
        s.SuggestedContentTypePropertyAlias,
    ),
    sourceType: norm(
      s.suggestedSourceType ?? s.sourceType ?? s.SourceType ?? s.SuggestedSourceType ?? 'property',
    ),
    nestedType: norm(
      s.suggestedNestedSchemaTypeName ??
        s.nestedSchemaTypeName ??
        s.NestedSchemaTypeName ??
        s.SuggestedNestedSchemaTypeName,
    ),
    resolver: parseResolver(
      s.suggestedResolverConfig ?? s.resolverConfig ?? s.ResolverConfig ?? s.SuggestedResolverConfig,
    ),
    sourceContentType: norm(
      s.suggestedSourceContentTypeAlias ??
        s.sourceContentTypeAlias ??
        s.SourceContentTypeAlias ??
        s.SuggestedSourceContentTypeAlias,
    ),
    confidence: s.confidence ?? s.Confidence ?? null,
  };
}

/** Only count gold mappings that actually have a target (skip unmapped placeholders). */
function goldTargets(gold) {
  return gold.mappings
    .filter((m) => m.schemaProp && (m.contentProp || m.sourceType !== 'property'))
    .map((m) => ({
      schemaProp: norm(m.schemaProp),
      contentProp: norm(m.contentProp),
      sourceType: norm(m.sourceType),
      nestedType: norm(m.nestedType),
      sourceContentType: norm(m.sourceContentType),
      resolver: m.resolverConfig ?? null,
      isSelfContainedRich: m.isSelfContainedRich,
      isCrossNodeRich: m.isCrossNodeRich,
    }));
}

const CROSS_NODE = new Set(['parent', 'ancestor', 'sibling']);

/** The `routes` list of a resolver config, case-insensitive on the key like the core parser. */
function routesOf(resolver) {
  const r = resolver?.routes ?? resolver?.Routes;
  return Array.isArray(r) ? r : null;
}
const propertyMappingsOf = (route) => {
  const p = route?.propertyMappings ?? route?.PropertyMappings;
  return Array.isArray(p) ? p : [];
};
const field = (obj, ...names) => {
  for (const n of names) if (obj?.[n] != null) return obj[n];
  return null;
};

/**
 * One nested binding (a propertyMappings entry) reproduced: same schemaProperty ->
 * contentProperty, and, when gold goes deeper on that field, the same deeper shape: nested
 * routes compared recursively, or extractAs stringList with the same nestedContentProperty.
 * Extra candidate detail (wrapInType, transforms, requiredProperties) is not compared: the
 * core infers wrapInProperty at render time and gold does not always spell it out.
 */
function bindingCovered(candBindings, gb) {
  const sp = norm(field(gb, 'schemaProperty', 'SchemaProperty'));
  const cp = norm(field(gb, 'contentProperty', 'ContentProperty'));
  const cb = candBindings.find(
    (b) =>
      norm(field(b, 'schemaProperty', 'SchemaProperty')) === sp &&
      norm(field(b, 'contentProperty', 'ContentProperty')) === cp,
  );
  if (!cb) return false;

  const goldInner = routesOf(gb);
  if (goldInner) return routesCover(routesOf(cb), goldInner);

  if (norm(field(gb, 'extractAs', 'ExtractAs')) === 'stringlist') {
    return (
      norm(field(cb, 'extractAs', 'ExtractAs')) === 'stringlist' &&
      norm(field(cb, 'nestedContentProperty', 'NestedContentProperty')) ===
        norm(field(gb, 'nestedContentProperty', 'NestedContentProperty'))
    );
  }
  return true;
}

/**
 * Every gold route reproduced by a candidate route for the same block alias: nested type
 * equal (case-insensitive) and every gold binding covered (extra candidate routes and extra
 * bindings allowed, as for the flat shapes). A missing candidate routes list never covers.
 */
function routesCover(candRoutes, goldRoutes) {
  if (!Array.isArray(candRoutes)) return false;
  return goldRoutes.every((gr) => {
    const alias = norm(field(gr, 'blockAlias', 'BlockAlias'));
    const cr = candRoutes.find((r) => norm(field(r, 'blockAlias', 'BlockAlias')) === alias);
    if (!cr) return false;
    if (norm(field(cr, 'nestedSchemaType', 'NestedSchemaType')) !== norm(field(gr, 'nestedSchemaType', 'NestedSchemaType')))
      return false;
    const candBindings = propertyMappingsOf(cr);
    return propertyMappingsOf(gr).every((gb) => bindingCovered(candBindings, gb));
  });
}

/**
 * A legacy nestedMappings gold row satisfied by a routes candidate: every route lands on
 * the gold nested type (the core applies a legacy list to every block, so a per-block
 * split is only equivalent when the types agree) and the union of the routes' bindings
 * covers the gold bindings.
 */
function routesCoverLegacy(candRoutes, goldNestedType, goldResolver) {
  if (!Array.isArray(candRoutes) || candRoutes.length === 0) return false;
  if (!candRoutes.every((r) => norm(field(r, 'nestedSchemaType', 'NestedSchemaType')) === goldNestedType)) return false;
  const union = {};
  for (const r of candRoutes) Object.assign(union, bindings({ nestedMappings: propertyMappingsOf(r) }, 'nestedMappings'));
  return coversBindings(union, bindings(goldResolver, 'nestedMappings'));
}

function lenientEq(a, b) {
  return a.schemaProp === b.schemaProp && a.contentProp === b.contentProp;
}

// Shape-aware strict match. Flat/property/built-in compare schemaProp+contentProp+sourceType.
// Rich shapes verify the load-bearing inner bindings instead of (complexType) or in addition
// to (blockContent) the parent alias — per the JsonLdGenerator/BlockContentResolver runtime.
function strictEq(a, b) {
  if (a.schemaProp !== b.schemaProp) return false;

  if (b.sourceType === 'complextype') {
    if (a.sourceType !== 'complextype') return false;
    if (norm(a.nestedType) !== norm(b.nestedType)) return false;
    return coversBindings(bindings(a.resolver, 'complexTypeMappings'), bindings(b.resolver, 'complexTypeMappings'));
  }

  if (b.sourceType === 'blockcontent') {
    if (a.sourceType !== 'blockcontent') return false;
    if (a.contentProp !== b.contentProp) return false; // the block-list property IS read at runtime
    const goldStr = stringListProp(b.resolver);
    if (goldStr !== null) {
      // stringList: candidate must also be stringList (no nestedType) with the same inner prop
      if (a.nestedType) return false;
      return stringListProp(a.resolver) === goldStr;
    }
    // per-block routes (v2): compared route by route, recursively; a candidate without
    // routes cannot satisfy them (the core ignores the mapping-level nested type once
    // routes are present, so there is no flat equivalent)
    const goldRoutes = routesOf(b.resolver);
    if (goldRoutes) return routesCover(routesOf(a.resolver), goldRoutes);
    // nested object shape: nestedType + inner nestedMappings must be reproduced, or a
    // routes candidate whose routes all land on that type and jointly cover the bindings
    const candRoutes = routesOf(a.resolver);
    if (candRoutes) return routesCoverLegacy(candRoutes, norm(b.nestedType), b.resolver);
    if (norm(a.nestedType) !== norm(b.nestedType)) return false;
    return coversBindings(bindings(a.resolver, 'nestedMappings'), bindings(b.resolver, 'nestedMappings'));
  }

  // flat property / static / built-in / cross-node: the parent alias + sourceType are load-bearing
  if (a.contentProp !== b.contentProp) return false;
  if (a.sourceType !== b.sourceType) return false;
  if (b.nestedType && a.nestedType !== b.nestedType) return false;
  // cross-node (v2): the source content type is load-bearing too. `parent` ignores it at
  // runtime (it reads whichever node is the actual parent), but a suggestion naming the
  // wrong type is still a wrong suggestion, so every relation is held to the same bar.
  if (CROSS_NODE.has(b.sourceType) && b.sourceContentType && a.sourceContentType !== b.sourceContentType) return false;
  return true;
}

function prf(tp, candCount, goldCount) {
  const precision = candCount ? tp / candCount : 0;
  const recall = goldCount ? tp / goldCount : 0;
  const f1 = precision + recall ? (2 * precision * recall) / (precision + recall) : 0;
  return { precision: round(precision), recall: round(recall), f1: round(f1), tp, candCount, goldCount };
}

const round = (n) => Math.round(n * 1000) / 1000;

/**
 * Score a candidate's suggestions for one content type against its gold mapping.
 */
export function scoreOne(gold, rawSuggestions) {
  const targets = goldTargets(gold);
  const cand = rawSuggestions
    .map(normaliseSuggestion)
    .filter((s) => s.schemaProp && (s.contentProp || s.sourceType !== 'property'));

  // de-dup candidates by schemaProp (keep first / highest-confidence already ordered upstream)
  const seen = new Set();
  const candUnique = cand.filter((s) => (seen.has(s.schemaProp) ? false : seen.add(s.schemaProp)));

  const lenientTP = targets.filter((g) => candUnique.some((c) => lenientEq(c, g))).length;
  const strictTP = targets.filter((g) => candUnique.some((c) => strictEq(c, g))).length;

  const richGold = targets.filter((g) => g.isSelfContainedRich);
  const richHit = richGold.filter((g) => candUnique.some((c) => strictEq(c, g)));
  const richMissed = richGold
    .filter((g) => !candUnique.some((c) => strictEq(c, g)))
    .map((g) => `${g.schemaProp}<-${g.sourceType}:${g.contentProp}${g.nestedType ? '/' + g.nestedType : ''}`);

  const crossGold = targets.filter((g) => g.isCrossNodeRich);
  const crossHit = crossGold.filter((g) => candUnique.some((c) => strictEq(c, g)));
  const crossMissed = crossGold
    .filter((g) => !candUnique.some((c) => strictEq(c, g)))
    .map((g) => `${g.schemaProp}<-${g.sourceType}:${g.sourceContentType ?? '*'}.${g.contentProp}`);

  return {
    alias: gold.alias,
    schemaType: gold.schemaType,
    lenient: prf(lenientTP, candUnique.length, targets.length),
    strict: prf(strictTP, candUnique.length, targets.length),
    rich: { goldCount: richGold.length, hit: richHit.length, missed: richMissed },
    crossNode: { goldCount: crossGold.length, hit: crossHit.length, missed: crossMissed },
  };
}

/** Aggregate per-type scores into a single report row per candidate (e.g. "ai" vs "heuristic"). */
export function aggregate(perType) {
  const sum = (sel) => perType.reduce((a, r) => a + sel(r), 0);
  const richGold = sum((r) => r.rich.goldCount);
  const richHit = sum((r) => r.rich.hit);
  const crossGold = sum((r) => r.crossNode.goldCount);
  const crossHit = sum((r) => r.crossNode.hit);
  const macro = (sel) => round(perType.reduce((a, r) => a + sel(r), 0) / (perType.length || 1));
  return {
    types: perType.length,
    richCoverage: { goldCount: richGold, hit: richHit, pct: round(richGold ? richHit / richGold : 0) },
    crossNodeCoverage: { goldCount: crossGold, hit: crossHit, pct: round(crossGold ? crossHit / crossGold : 0) },
    strictF1_macro: macro((r) => r.strict.f1),
    lenientF1_macro: macro((r) => r.lenient.f1),
    // avg candidate count per type — track alongside F1 so a terser prompt can't game precision
    avgCandidates: macro((r) => r.strict.candCount),
  };
}
