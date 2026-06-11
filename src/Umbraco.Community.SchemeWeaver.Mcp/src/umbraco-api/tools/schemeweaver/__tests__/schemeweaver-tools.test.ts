/**
 * SchemeWeaver Tool Integration Tests
 *
 * Run against a live Umbraco instance (the SchemeWeaver TestHost on
 * https://localhost:44308) with the API user credentials from .env.
 *
 * The mapping round-trip test deliberately picks a content type that has no
 * existing mapping and deletes its mapping afterwards, so the TestHost's
 * fixture mappings are never disturbed.
 */

import { setupTestEnvironment, createMockRequestHandlerExtra } from "./setup.js";
import searchSchemaTypesTool from "../get/search-schema-types.js";
import getSchemaTypePropertiesTool from "../get/get-schema-type-properties.js";
import listContentTypesTool from "../get/list-content-types.js";
import getContentTypePropertiesTool from "../get/get-content-type-properties.js";
import getAllSchemaMappingsTool from "../get/get-all-schema-mappings.js";
import getSchemaMappingTool from "../get/get-schema-mapping.js";
import suggestPropertyMappingsTool from "../post/suggest-property-mappings.js";
import saveSchemaMappingTool from "../post/save-schema-mapping.js";
import previewJsonLdTool from "../post/preview-json-ld.js";
import deleteSchemaMappingTool from "../delete/delete-schema-mapping.js";

describe("schemeweaver tools", () => {
  setupTestEnvironment();
  const context = createMockRequestHandlerExtra();

  it("search-schema-types filters by search text", async () => {
    const result = await searchSchemaTypesTool.handler({ search: "blog" }, context);

    expect(result.isError).toBeFalsy();
    const { items } = result.structuredContent as { items: Array<{ name: string }> };
    const names = items.map((t) => t.name);
    expect(names).toContain("Blog");
    expect(names).toContain("BlogPosting");
    expect(names.every((n) => n.toLowerCase().includes("blog"))).toBe(true);
  });

  it("get-schema-type-properties returns ranked properties for Article", async () => {
    const result = await getSchemaTypePropertiesTool.handler(
      { name: "Article", ranked: true },
      context
    );

    expect(result.isError).toBeFalsy();
    const { items } = result.structuredContent as {
      items: Array<{ name: string; confidence: number; isComplexType: boolean }>;
    };
    // Property names come back PascalCase from this endpoint
    const headline = items.find((p) => p.name.toLowerCase() === "headline");
    expect(headline).toBeDefined();
    expect(headline!.confidence).toBeGreaterThan(0);
    const author = items.find((p) => p.name.toLowerCase() === "author");
    expect(author?.isComplexType).toBe(true);
  });

  it("list-content-types returns aliases and keys", async () => {
    const result = await listContentTypesTool.handler(context);

    expect(result.isError).toBeFalsy();
    const { items } = result.structuredContent as {
      items: Array<{ alias: string; key: string; propertyCount: number }>;
    };
    expect(items.length).toBeGreaterThan(0);
    expect(items[0].alias).toBeTruthy();
    expect(items[0].key).toBeTruthy();
  });

  it("get-content-type-properties includes built-in properties", async () => {
    const listResult = await listContentTypesTool.handler(context);
    const { items } = listResult.structuredContent as { items: Array<{ alias: string }> };

    const result = await getContentTypePropertiesTool.handler(
      { alias: items[0].alias },
      context
    );

    expect(result.isError).toBeFalsy();
    const { items: properties } = result.structuredContent as {
      items: Array<{ alias: string; editorAlias: string }>;
    };
    const aliases = properties.map((p) => p.alias);
    expect(aliases).toContain("__name");
    expect(aliases).toContain("__url");
  });

  it("get-schema-mapping returns an error for an unmapped alias", async () => {
    const result = await getSchemaMappingTool.handler(
      { contentTypeAlias: "definitely-not-a-real-alias" },
      context
    );

    expect(result.isError).toBe(true);
  });

  it("completes the full mapping workflow: suggest -> save -> get -> preview -> delete", async () => {
    // Find a content type without an existing mapping so fixtures stay intact
    const [contentTypesResult, mappingsResult] = await Promise.all([
      listContentTypesTool.handler(context),
      getAllSchemaMappingsTool.handler(context),
    ]);
    const contentTypes = (contentTypesResult.structuredContent as {
      items: Array<{ alias: string; key: string }>;
    }).items;
    const mappedAliases = new Set(
      (mappingsResult.structuredContent as { items: Array<{ contentTypeAlias: string }> }).items.map(
        (m) => m.contentTypeAlias
      )
    );
    const unmapped = contentTypes.find((ct) => !mappedAliases.has(ct.alias));
    expect(unmapped).toBeDefined();

    try {
      // Heuristic baseline
      const suggestResult = await suggestPropertyMappingsTool.handler(
        { contentTypeAlias: unmapped!.alias, schemaTypeName: "Article" },
        context
      );
      expect(suggestResult.isError).toBeFalsy();
      const suggestions = (suggestResult.structuredContent as {
        items: Array<{ schemaPropertyName: string; confidence: number }>;
      }).items;
      expect(suggestions.length).toBeGreaterThan(0);

      // Save a reasoned mapping (headline from the built-in name property)
      const saveResult = await saveSchemaMappingTool.handler(
        {
          contentTypeAlias: unmapped!.alias,
          contentTypeKey: unmapped!.key,
          schemaTypeName: "Article",
          isEnabled: true,
          isInherited: false,
          idOverride: null,
          propertyMappings: [
            {
              schemaPropertyName: "headline",
              sourceType: "property" as const,
              contentTypePropertyAlias: "__name",
              isAutoMapped: false,
            },
            {
              schemaPropertyName: "url",
              sourceType: "property" as const,
              contentTypePropertyAlias: "__url",
              isAutoMapped: false,
            },
          ],
        },
        context
      );
      expect(saveResult.isError).toBeFalsy();

      // Read it back
      const getResult = await getSchemaMappingTool.handler(
        { contentTypeAlias: unmapped!.alias },
        context
      );
      expect(getResult.isError).toBeFalsy();
      const mapping = getResult.structuredContent as {
        schemaTypeName: string;
        propertyMappings: Array<{ schemaPropertyName: string }>;
      };
      expect(mapping.schemaTypeName).toBe("Article");
      expect(mapping.propertyMappings.map((p) => p.schemaPropertyName)).toEqual(
        expect.arrayContaining(["headline", "url"])
      );

      // Mock preview (no contentKey) proves JSON-LD structure generation
      const previewResult = await previewJsonLdTool.handler(
        { contentTypeAlias: unmapped!.alias, contentKey: undefined, culture: undefined },
        context
      );
      expect(previewResult.isError).toBeFalsy();
      const preview = previewResult.structuredContent as { jsonLd: string };
      expect(preview.jsonLd).toContain('"Article"');
    } finally {
      // Always clean up the test mapping
      await deleteSchemaMappingTool.handler({ contentTypeAlias: unmapped!.alias }, context);
    }

    const afterDelete = await getSchemaMappingTool.handler(
      { contentTypeAlias: unmapped!.alias },
      context
    );
    expect(afterDelete.isError).toBe(true);
  });
});
