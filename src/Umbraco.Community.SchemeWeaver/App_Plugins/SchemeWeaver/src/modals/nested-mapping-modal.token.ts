import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

/**
 * Data for the flat block-mapping panel. Unlike the old 3-step wizard (one
 * block type, one nested schema type), this panel manages the WHOLE block-list
 * property: every block element type it contains, each routed to its own
 * Schema.org type and target page property.
 */
export interface NestedMappingModalData {
  /** The page content type that owns the block-list property. */
  contentTypeAlias: string;
  /** The block-list property alias being configured. */
  contentTypePropertyAlias: string;
  /**
   * Existing saved `blockContent` mappings for THIS block-list property — one
   * per target page property — used to pre-fill the panel when re-editing.
   * Each carries the target `schemaPropertyName` and the routed `resolverConfig`
   * (JSON `{ routes: [...] }`, or a legacy flat shape).
   */
  existingMappings?: Array<{ schemaPropertyName: string; nestedSchemaTypeName?: string | null; resolverConfig: string | null }>;
}

/** One saved target mapping the panel emits — turns into one PropertyMappingDto. */
export interface NestedMappingModalTargetMapping {
  /** Target page Schema.org property (mainEntity | hasPart | about | …). */
  schemaPropertyName: string;
  /** The block-list property alias (same for every emitted mapping). */
  contentTypePropertyAlias: string;
  /** Serialised routed ResolverConfig — JSON `{ routes: [...] }`. */
  resolverConfig: string;
}

export interface NestedMappingModalValue {
  /** One entry per target page property, grouped from the panel's block rows. */
  mappings: NestedMappingModalTargetMapping[];
}

export const SCHEMEWEAVER_NESTED_MAPPING_MODAL = new UmbModalToken<
  NestedMappingModalData,
  NestedMappingModalValue
>('SchemeWeaver.Modal.NestedMapping', {
  modal: {
    type: 'sidebar',
    size: 'large',
  },
});
