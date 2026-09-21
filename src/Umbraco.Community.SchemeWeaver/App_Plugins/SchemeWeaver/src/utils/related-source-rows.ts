import type { ContentTypeInfo, ContentTypeProperty } from '../api/types.js';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import { SourceType } from '../constants/source-type.js';

/**
 * The two look-ups hydration needs. `SchemeWeaverContext` (the workspace view)
 * and `SchemeWeaverRepository` (the property-mapping modal) both satisfy it
 * structurally, so one helper serves every host without a shared instance.
 */
export interface RelatedSourceLookup {
  requestContentTypeProperties(contentTypeAlias: string): Promise<ContentTypeProperty[] | undefined>;
  requestContentTypes(): Promise<ContentTypeInfo[] | undefined>;
}

/** Whether a source type reads its value from a related node rather than the page itself. */
export function isRelatedSourceType(sourceType: string): boolean {
  return sourceType === SourceType.Parent || sourceType === SourceType.Ancestor || sourceType === SourceType.Sibling;
}

/** The distinct related content types the rows name, in first-seen order. */
export function relatedSourceAliases(rows: readonly PropertyMappingRow[]): string[] {
  return [...new Set(
    rows
      .filter((row) => row.sourceContentTypeAlias && isRelatedSourceType(row.sourceType))
      .map((row) => row.sourceContentTypeAlias),
  )];
}

/**
 * Hydrates every parent / ancestor / sibling row that names a related content
 * type with that type's property aliases (`sourceContentTypeProperties`, which
 * populates the property dropdown) and its document-type key
 * (`sourceDocumentTypeUnique`, which the document-type picker shows as its
 * selection). Every other row comes back as the same object, and the same
 * array comes back when there is nothing to hydrate.
 *
 * One helper for the three places rows enter the table: loading a saved
 * mapping, the workspace view's auto-map, and the modal's initial and AI
 * auto-map. Only the first of those hydrated before, so an auto-mapped
 * cross-node row (`publisher <- ancestor homePage.organisationName`) arrived
 * with its alias set but rendered an empty picker and no dropdown, and the
 * user had to browse for the type the mapper had already chosen. The alias is
 * what the save path persists either way; hydration is purely what the row
 * needs to render as the saved row would.
 *
 * Never throws. A related type that cannot be read (deleted since the mapping
 * was saved, a transient failure) leaves its rows unhydrated with the alias
 * intact: by the repository's error-handling policy an affordance degrades, a
 * mapping is never lost.
 */
export async function enrichRelatedSourceRows(
  rows: PropertyMappingRow[],
  lookup: RelatedSourceLookup,
): Promise<PropertyMappingRow[]> {
  const aliases = relatedSourceAliases(rows);
  if (aliases.length === 0) return rows;

  const propertiesByAlias = new Map<string, string[]>();
  await Promise.all(
    aliases.map(async (alias) => {
      try {
        const properties = await lookup.requestContentTypeProperties(alias);
        if (properties) propertiesByAlias.set(alias, properties.map((p) => p.alias));
      } catch {
        // Boundary catch by policy: the row keeps its alias; only its dropdown stays empty.
      }
    }),
  );

  // One listing serves every alias: it is how an alias becomes the picker's key.
  // Aliases are compared case-insensitively, as the server-side resolvers do.
  let keyByAlias = new Map<string, string>();
  try {
    const contentTypes = await lookup.requestContentTypes();
    keyByAlias = new Map((contentTypes ?? []).map((ct) => [ct.alias.toLowerCase(), ct.key]));
  } catch {
    // Boundary catch by policy: the picker shows no selection; the alias still saves.
  }

  return rows.map((row) => {
    if (!row.sourceContentTypeAlias || !isRelatedSourceType(row.sourceType)) return row;
    const properties = propertiesByAlias.get(row.sourceContentTypeAlias);
    const unique = keyByAlias.get(row.sourceContentTypeAlias.toLowerCase());
    if (properties === undefined && unique === undefined) return row;
    return {
      ...row,
      sourceContentTypeProperties: properties ?? row.sourceContentTypeProperties,
      sourceDocumentTypeUnique: unique ?? row.sourceDocumentTypeUnique,
    };
  });
}
