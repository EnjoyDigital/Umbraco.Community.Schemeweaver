/**
 * Source type discriminator for property mappings.
 *
 * These values are the wire-format strings sent to / received from the C# API
 * (lowercase, see `CLAUDE.md` → Key Patterns). Centralising them here prevents
 * typos across the codebase and lets tests reference `SourceType.Property`
 * instead of raw string literals.
 */
export const SourceType = {
  Property: 'property',
  Static: 'static',
  Parent: 'parent',
  Ancestor: 'ancestor',
  Sibling: 'sibling',
  BlockContent: 'blockContent',
  ComplexType: 'complexType',
  Reference: 'reference',
} as const;

export type SourceTypeValue = (typeof SourceType)[keyof typeof SourceType];
