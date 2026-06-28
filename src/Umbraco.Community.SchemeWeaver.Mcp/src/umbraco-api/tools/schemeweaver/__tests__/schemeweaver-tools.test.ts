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
import getRenderedJsonLdTool from "../get/get-rendered-json-ld.js";
import getUsyncDriftTool from "../get/get-usync-drift.js";
import exportMappingsToUsyncTool from "../post/export-mappings-to-usync.js";
import getServerInfoTool from "../../umbraco-server/get/get-server-info.js";

const DRIFT_CODES = ["in-sync", "db-only", "disk-only", "content-differs", "usync-unavailable"];

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
        { contentTypeAlias: unmapped!.alias, contentKey: undefined, blockInstanceKey: undefined, culture: undefined },
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

  // ==========================================================================
  // HOST-DEPENDENT: Delivery API + base-URL transparency.
  // These need a live TestHost (https://localhost:44308) with the Delivery API
  // enabled. They are authored for the leader/CI to run against the TestHost and
  // are NOT executed in the worktree.
  // ==========================================================================

  it("get-rendered-json-ld returns live JSON-LD for '/' (HOST-DEPENDENT)", async () => {
    const result = await getRenderedJsonLdTool.handler(
      { route: "/", scope: undefined, culture: undefined },
      context
    );

    expect(result.isError).toBeFalsy();
    const out = result.structuredContent as {
      requestUrl: string;
      httpStatus: number;
      jsonLd: { schemaOrg?: unknown };
      note: string;
    };
    expect(out.httpStatus).toBe(200);
    expect(out.requestUrl).toContain(
      "/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route"
    );
    // Body is an object { schemaOrg: [...] }, NOT a bare array (DA-FIX 4).
    expect(Array.isArray(out.jsonLd.schemaOrg)).toBe(true);
    expect((out.jsonLd.schemaOrg as unknown[]).length).toBeGreaterThan(0);
  });

  it("get-rendered-json-ld surfaces an empty graph rather than claiming success (HOST-DEPENDENT)", async () => {
    // A published page with no SchemeWeaver mapping: HTTP 200 + empty schemaOrg.
    const result = await getRenderedJsonLdTool.handler(
      { route: "/", scope: "page", culture: undefined },
      context
    );

    expect(result.isError).toBeFalsy();
    const out = result.structuredContent as { note: string; jsonLd: { schemaOrg?: unknown[] } };
    const blocks = out.jsonLd.schemaOrg ?? [];
    if (blocks.length === 0) {
      // Empty-graph must be visible, never silently reported as ground truth.
      expect(out.note).toContain("ZERO JSON-LD blocks");
    }
  });

  it("get-rendered-json-ld surfaces status for a bogus route without throwing (HOST-DEPENDENT)", async () => {
    const result = await getRenderedJsonLdTool.handler(
      { route: "/definitely-not-a-real-route-xyz", scope: undefined, culture: undefined },
      context
    );

    // Non-2xx is surfaced as data, never thrown: 404/401/200-empty depending on config.
    expect(result.isError).toBeFalsy();
    const out = result.structuredContent as { httpStatus: number; requestUrl: string };
    expect(typeof out.httpStatus).toBe("number");
    expect(out.requestUrl).toContain("/json-ld/by-route");
  });

  it("preview-json-ld reports backoffice context once the backend ships the fields (HOST-DEPENDENT)", async () => {
    const result = await previewJsonLdTool.handler(
      { contentTypeAlias: "definitely-not-a-real-alias", contentKey: undefined, blockInstanceKey: undefined, culture: undefined },
      context
    );

    // Soft-guarded: context/resolvedBaseUrl are only present after the backend
    // ships them — this assertion is meaningful only then.
    const preview = result.structuredContent as {
      context?: string;
      resolvedBaseUrl?: string;
    } | null;
    if (preview && preview.context) {
      expect(preview.context).toBe("backoffice-preview");
      expect(typeof preview.resolvedBaseUrl).toBe("string");
    }
  });

  it("get-server-info reports the configured base URL (HOST-DEPENDENT)", async () => {
    const result = await getServerInfoTool.handler(context);

    expect(result.isError).toBeFalsy();
    const info = result.structuredContent as { configuredBaseUrl: string };
    expect(info.configuredBaseUrl).toBe(
      (process.env.UMBRACO_BASE_URL || "https://localhost:44308").replace(/\/+$/, "")
    );
  });

  it("get-server-info distinguishes the TestHost sandbox (HOST-DEPENDENT)", async () => {
    const result = await getServerInfoTool.handler(context);

    expect(result.isError).toBeFalsy();
    const info = result.structuredContent as { hasPublishedContent?: boolean; isTestHost?: boolean };
    // The target is the SchemeWeaver TestHost, so it should self-identify.
    expect(info.isTestHost).toBe(true);
    expect(typeof info.hasPublishedContent).toBe("boolean");
  });

  it("get-usync-drift returns a drift report with valid status codes (HOST-DEPENDENT)", async () => {
    const result = await getUsyncDriftTool.handler(context);

    expect(result.isError).toBeFalsy();
    const report = result.structuredContent as {
      usyncAvailable: boolean;
      items: Array<{ contentTypeAlias: string; status: string }>;
    };
    expect(typeof report.usyncAvailable).toBe("boolean");
    expect(Array.isArray(report.items)).toBe(true);
    for (const item of report.items) {
      expect(DRIFT_CODES).toContain(item.status);
    }
  });

  it("export-mappings-to-usync with an unknown alias writes nothing and does not throw (HOST-DEPENDENT)", async () => {
    // Targeting a non-existent alias keeps the TestHost's committed fixture .config files untouched.
    const result = await exportMappingsToUsyncTool.handler(
      { contentTypeAlias: "definitely-not-a-real-alias-xyz" },
      context
    );

    expect(result.isError).toBeFalsy();
    const out = result.structuredContent as {
      usyncAvailable: boolean;
      items: Array<{ alias: string; written: boolean }>;
    };
    expect(typeof out.usyncAvailable).toBe("boolean");
    // No mapping for the alias → nothing exported.
    expect(out.items).toHaveLength(0);
  });
});
