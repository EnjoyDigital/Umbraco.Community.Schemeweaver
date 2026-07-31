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

/**
 * Localisation key for a source type's human-readable label. Shared by the
 * mapping table and the change-schema-type confirmation so neither ever renders
 * a raw wire value like `complexType` at the user.
 */
export function sourceTypeLabelKey(sourceType: string): string {
  switch (sourceType) {
    case SourceType.Property: return 'schemeWeaver_sourceCurrentNode';
    case SourceType.Static: return 'schemeWeaver_sourceStaticValue';
    case SourceType.Parent: return 'schemeWeaver_sourceParentNode';
    case SourceType.Ancestor: return 'schemeWeaver_sourceAncestorNode';
    case SourceType.Sibling: return 'schemeWeaver_sourceSiblingNode';
    case SourceType.BlockContent: return 'schemeWeaver_sourceBlockContent';
    case SourceType.ComplexType: return 'schemeWeaver_sourceComplexType';
    case SourceType.Reference: return 'schemeWeaver_sourceReference';
    default: return 'schemeWeaver_sourceCurrentNode';
  }
}
