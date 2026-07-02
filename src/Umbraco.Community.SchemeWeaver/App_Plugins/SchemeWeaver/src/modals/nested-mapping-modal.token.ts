import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

/**
 * Data for the block-mapping panel, scoped to ONE parent property-mapping row.
 *
 * The panel maps the block element types of a Block List/Grid property INTO the
 * parent row's Schema.org property (`schemaPropertyName`). That target is fixed
 * context — the panel never changes which page property the output lands in;
 * that is decided on the main mapping table. Multi-target fan-out is expressed
 * as separate rows on the main table (each with its own panel session), matching
 * the backend model where each PropertyMappingDto row is one target.
 */
export interface NestedMappingModalData {
  /** The page content type that owns the block-list property. */
  contentTypeAlias: string;
  /** The block-list property alias being configured. */
  contentTypePropertyAlias: string;
  /**
   * The parent row's Schema.org property the blocks map into (e.g. `review`).
   * Immutable context.
   */
  schemaPropertyName: string;
  /** Display type of the parent schema property (e.g. `Review`), when known. */
  schemaPropertyType?: string;
  /**
   * Schema.org object types accepted by the parent property (e.g. `["Review"]`).
   * Constrains the per-block nested-type picker; empty/`Thing` → free search.
   */
  acceptedTypes?: string[];
  /**
   * THIS row's saved `resolverConfig` — routed (`{ routes: [...] }`), legacy flat
   * (`{ nestedMappings: [...] }`), or a string-list extraction shape. `null` for a
   * freshly added row (the panel then seeds from the heuristic suggester).
   */
  existingConfig: string | null;
  /**
   * The row's legacy mapping-level nested type (`NestedSchemaTypeName`) — the
   * implicit single-route type used by legacy flat configs. Needed to seed the
   * panel for legacy shapes; ignored when `existingConfig` carries `routes`.
   */
  nestedSchemaTypeName?: string | null;
  /**
   * Blocks already routed by SIBLING rows on the same block-list property
   * (other targets). Shown as read-only context so the same block isn't
   * accidentally emitted under two properties; never edited from this panel.
   */
  siblingClaims?: NestedMappingModalSiblingClaim[];
}

/** Blocks a sibling row (another target on the same block-list property) has routed. */
export interface NestedMappingModalSiblingClaim {
  /** The sibling row's target Schema.org property (e.g. `mainEntity`). */
  schemaPropertyName: string;
  /** Block element type aliases that sibling routes. */
  blockAliases: string[];
}

/**
 * An explicit fan-out emitted by the panel's "create rows for other properties"
 * affordance (off-target auto-map suggestions). The caller merges each entry into
 * an existing sibling row or creates a new row — it must never touch the opened row.
 */
export interface NestedMappingModalAdditionalTarget {
  /** Target page Schema.org property for the new/merged sibling row. */
  schemaPropertyName: string;
  /** Serialised routed ResolverConfig — JSON `{ routes: [...] }` — for that sibling row. */
  resolverConfig: string;
}

export interface NestedMappingModalValue {
  /**
   * The opened row's serialised ResolverConfig. When the user made no changes this
   * is `data.existingConfig` VERBATIM (byte-fidelity — a no-change save must be a
   * persistence no-op). `null` when no blocks are mapped and none were before.
   */
  resolverConfig: string | null;
  /** Explicit fan-out to other targets; empty unless the user opted in. */
  additionalTargets: NestedMappingModalAdditionalTarget[];
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
