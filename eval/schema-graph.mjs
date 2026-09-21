// The Schema.org type graph, read from the registry dump produced by the SchemaDump
// utility (which serialises SchemeWeaver's own SchemaTypeRegistry, so the ranges and
// property names here are exactly the ones the runtime uses).
//
// SCHEMA.NET MULTIPLE INHERITANCE
// -------------------------------
// Schema.org types can have several supertypes; C# cannot, so Schema.NET synthesises
// an abstract combining class and names it after its parts — HowToStep's base type is
// `CreativeWorkAndItemListAndListItem`. Those combining classes are abstract, so the
// registry (which scans non-abstract types) never lists them, leaving a dangling parent
// name. We resolve such a name by splitting it on `And` into its component types.
//
// The split is applied ONLY to parent names absent from the registry. Real Schema.org
// types whose own names contain "And" (BedAndBreakfast, HealthAndBeautyBusiness,
// HomeAndConstructionBusiness, TypeAndQuantityNode) are present, so they are never split.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';

export const TYPES = JSON.parse(
  readFileSync(join(process.cwd(), 'eval/cache/schema-types.json'), 'utf8'),
);

/** Split a dangling (abstract, synthesised) parent name into its component types. */
function splitSynthetic(name) {
  if (!name || TYPES[name]) return name ? [name] : [];
  return name
    .split(/And(?=[A-Z])/)
    .map((s) => s.trim())
    .filter((s) => TYPES[s]);
}

/** Direct supertypes of a type, with synthesised combining classes resolved. */
export function parentsOf(typeName) {
  const rec = TYPES[typeName];
  if (!rec?.parent) return [];
  return splitSynthetic(rec.parent);
}

/** typeName -> direct subtypes. Built once; combining classes are flattened away. */
export const CHILDREN = (() => {
  const kids = new Map();
  for (const name of Object.keys(TYPES)) {
    for (const p of parentsOf(name)) {
      if (!kids.has(p)) kids.set(p, []);
      kids.get(p).push(name);
    }
  }
  for (const list of kids.values()) list.sort();
  return kids;
})();

export const childrenOf = (typeName) => CHILDREN.get(typeName) ?? [];

/** Is `candidate` the same as, or a subtype of, `ancestor`? */
export function isSubtypeOf(candidate, ancestor) {
  if (!candidate || !ancestor) return false;
  if (candidate.toLowerCase() === ancestor.toLowerCase()) return true;
  const seen = new Set();
  const stack = [candidate];
  while (stack.length) {
    const cur = stack.pop();
    if (seen.has(cur)) continue;
    seen.add(cur);
    for (const p of parentsOf(cur)) {
      if (p.toLowerCase() === ancestor.toLowerCase()) return true;
      stack.push(p);
    }
  }
  return false;
}

/** Properties of a type, as the registry reports them. */
export const propertiesOf = (typeName) => TYPES[typeName]?.properties ?? [];

/** One property record of a type, matched case-insensitively. */
export function propertyOf(typeName, propName) {
  return (
    propertiesOf(typeName).find((p) => p.name.toLowerCase() === String(propName).toLowerCase()) ?? null
  );
}

/**
 * The declared range of a schema property, filtered to types the registry knows.
 * Primitives (Text, URL, Number, DateTime, ...) drop out, which is what we want: they
 * are the "this is a plain value" case, handled as `property` rather than a nested object.
 */
export function rangeOf(typeName, propName) {
  const p = propertyOf(typeName, propName);
  return (p?.acceptedTypes ?? []).filter((t) => TYPES[t]);
}
