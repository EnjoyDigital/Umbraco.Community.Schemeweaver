/**
 * SchemeWeaver SEO Tool Integration Tests
 *
 * Run against the SchemeWeaver TestHost (which includes the Seo satellite) with the API user
 * from .env. The API user is in the Administrators group, which is granted the SEO Schema
 * section on first start. Every mutation targets state it restores afterwards.
 */

import { setupTestEnvironment, createMockRequestHandlerExtra } from "../../schemeweaver/__tests__/setup.js";
import getSeoContentKindsTool from "../get/get-seo-content-kinds.js";
import getSeoContentTypeStatusTool from "../get/get-seo-content-type-status.js";
import setupSeoContentTypeTool from "../post/setup-seo-content-type.js";
import saveSeoMappingTool from "../put/save-seo-mapping.js";
import getSiteIdentityTool from "../get/get-site-identity.js";
import setSiteIdentityTool from "../put/set-site-identity.js";
import getSeoPageSummaryTool from "../get/get-seo-page-summary.js";
import setSeoPageOverrideTool from "../put/set-seo-page-override.js";
import clearSeoPageOverrideTool from "../delete/clear-seo-page-override.js";
import runSeoHealthCheckTool from "../post/run-seo-health-check.js";
import getSchemaMappingTool from "../../schemeweaver/get/get-schema-mapping.js";
import saveSchemaMappingTool from "../../schemeweaver/post/save-schema-mapping.js";
import deleteSchemaMappingTool from "../../schemeweaver/delete/delete-schema-mapping.js";

type Status = { alias: string; status: string; publishedCount: number; isElement: boolean; mapping: { templateId: string | null; schemaTypeName: string; missingRequired: string[] } | null };

describe("schemeweaver-seo tools", () => {
  setupTestEnvironment();
  const context = createMockRequestHandlerExtra();

  it("get-seo-content-kinds lists the curated kinds with slots", async () => {
    const result = await getSeoContentKindsTool.handler(context);
    expect(result.isError).toBeFalsy();
    const { templates, retired, businessTypes } = result.structuredContent as {
      templates: Array<{ id: string; schemaType: string; slots: Array<{ schemaProperty: string; importance: string }> }>;
      retired: unknown[];
      businessTypes: unknown[];
    };
    const article = templates.find((t) => t.id === "article");
    expect(article?.schemaType).toBe("Article");
    expect(article?.slots.some((s) => s.schemaProperty === "headline" && s.importance === "required")).toBe(true);
    expect(retired.length).toBeGreaterThan(0);
    expect(businessTypes.length).toBeGreaterThan(0);
  });

  it("get-seo-content-type-status lists every type and reads one by alias", async () => {
    const all = await getSeoContentTypeStatusTool.handler({ contentTypeAlias: undefined }, context);
    expect(all.isError).toBeFalsy();
    const { items } = all.structuredContent as { items: Status[] };
    expect(items.length).toBeGreaterThan(0);
    const first = items[0];
    const one = await getSeoContentTypeStatusTool.handler({ contentTypeAlias: first.alias }, context);
    expect(one.isError).toBeFalsy();
    expect((one.structuredContent as { items: Status[] }).items[0].alias).toBe(first.alias);
  });

  it("setup + save-seo-mapping write an ordinary mapping the core tools read, then restore", async () => {
    const all = await getSeoContentTypeStatusTool.handler({ contentTypeAlias: undefined }, context);
    const { items } = all.structuredContent as { items: Status[] };
    const target = items.find((i) => i.alias === "blogArticle") ?? items.find((i) => !i.isElement && i.publishedCount > 0);
    expect(target).toBeDefined();
    const alias = target!.alias;

    const before = await getSchemaMappingTool.handler({ contentTypeAlias: alias }, context);
    const snapshot = before.isError ? null : (before.structuredContent as Record<string, unknown>);

    try {
      const setup = await setupSeoContentTypeTool.handler({ contentTypeAlias: alias, templateId: "article", schemaTypeName: "BlogPosting" }, context);
      expect(setup.isError).toBeFalsy();
      const projection = setup.structuredContent as { schemaTypeName: string; slots: Array<{ slot: { schemaProperty: string }; value: Record<string, unknown> }> };
      expect(projection.schemaTypeName).toBe("BlogPosting");
      const slots = Object.fromEntries(projection.slots.map((s) => [s.slot.schemaProperty, s.value]));
      slots.headline = { kind: "property", propertyAlias: "__name" };

      const saved = await saveSeoMappingTool.handler({ contentTypeAlias: alias, templateId: "article", schemaTypeName: "BlogPosting", slots: slots as never }, context);
      expect(saved.isError).toBeFalsy();
      expect((saved.structuredContent as Status).mapping?.templateId).toBe("article");

      const core = await getSchemaMappingTool.handler({ contentTypeAlias: alias }, context);
      expect(core.isError).toBeFalsy();
      const mapping = core.structuredContent as { schemaTypeName: string; propertyMappings: Array<{ schemaPropertyName: string; contentTypePropertyAlias: string | null }> };
      expect(mapping.schemaTypeName).toBe("BlogPosting");
      expect(mapping.propertyMappings.find((r) => r.schemaPropertyName === "headline")?.contentTypePropertyAlias).toBe("__name");
    } finally {
      if (snapshot) {
        await saveSchemaMappingTool.handler(snapshot as never, context);
      } else {
        await deleteSchemaMappingTool.handler({ contentTypeAlias: alias }, context);
      }
    }
  });

  it("site identity round-trips and reports isConfigured", async () => {
    const before = await getSiteIdentityTool.handler(context);
    const previous = (before.structuredContent ?? {}) as { isConfigured?: boolean } & Record<string, unknown>;
    try {
      const saved = await setSiteIdentityTool.handler(
        { kind: "organisation", name: "MCP Test Org", sameAs: ["https://www.linkedin.com/company/mcp-test"], openingHours: [] } as never,
        context
      );
      expect(saved.isError).toBeFalsy();
      expect((saved.structuredContent as { isConfigured: boolean; name: string }).name).toBe("MCP Test Org");
      const read = await getSiteIdentityTool.handler(context);
      expect((read.structuredContent as { isConfigured: boolean }).isConfigured).toBe(true);

      const invalid = await setSiteIdentityTool.handler({ kind: "organisation", name: "", sameAs: [], openingHours: [] } as never, context);
      expect(invalid.isError).toBeTruthy();
    } finally {
      if ((previous as { isConfigured?: boolean }).isConfigured) {
        await setSiteIdentityTool.handler(previous as never, context);
      } else {
        // No identity existed before: leave a harmless placeholder (the store has no "unset" API).
        await setSiteIdentityTool.handler({ kind: "organisation", name: "MCP Test Org", sameAs: [], openingHours: [] } as never, context);
      }
    }
  });

  it("page summary, override and clear follow a set-up page", async () => {
    const all = await getSeoContentTypeStatusTool.handler({ contentTypeAlias: undefined }, context);
    const { items } = all.structuredContent as { items: Status[] };
    const target = items.find((i) => i.alias === "blogArticle" && i.publishedCount > 0) ?? items.find((i) => !i.isElement && i.publishedCount > 0 && i.mapping?.templateId);
    if (!target) return;
    const before = await getSchemaMappingTool.handler({ contentTypeAlias: target.alias }, context);
    const snapshot = before.isError ? null : (before.structuredContent as Record<string, unknown>);
    try {
      await saveSeoMappingTool.handler(
        { contentTypeAlias: target.alias, templateId: "article", schemaTypeName: undefined, slots: { headline: { kind: "property", propertyAlias: "__name" } } as never },
        context
      );
      const health = await runSeoHealthCheckTool.handler({ force: true }, context);
      expect(health.isError).toBeFalsy();
      const report = health.structuredContent as { items: Array<{ alias: string; samplePageKey: string | null }> };
      const item = report.items.find((i) => i.alias === target.alias);
      expect(item?.samplePageKey).toBeTruthy();
      const key = item!.samplePageKey!;

      const summary = await getSeoPageSummaryTool.handler({ contentKey: key, culture: undefined }, context);
      expect((summary.structuredContent as { effectiveSchemaTypeName: string }).effectiveSchemaTypeName).toBe("Article");

      const retyped = await setSeoPageOverrideTool.handler({ contentKey: key, schemaTypeName: "NewsArticle", isExcluded: false }, context);
      expect(retyped.isError).toBeFalsy();
      const after = await getSeoPageSummaryTool.handler({ contentKey: key, culture: undefined }, context);
      expect((after.structuredContent as { effectiveSchemaTypeName: string }).effectiveSchemaTypeName).toBe("NewsArticle");

      const cleared = await clearSeoPageOverrideTool.handler({ contentKey: key }, context);
      expect(cleared.isError).toBeFalsy();
      const restored = await getSeoPageSummaryTool.handler({ contentKey: key, culture: undefined }, context);
      expect((restored.structuredContent as { override: unknown }).override).toBeNull();
    } finally {
      if (snapshot) {
        await saveSchemaMappingTool.handler(snapshot as never, context);
      } else {
        await deleteSchemaMappingTool.handler({ contentTypeAlias: target.alias }, context);
      }
    }
  });
});
