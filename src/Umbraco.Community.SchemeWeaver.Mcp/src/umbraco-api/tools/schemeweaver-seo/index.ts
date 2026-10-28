/**
 * SchemeWeaver SEO Tool Collection
 *
 * Tools for the SchemeWeaver SEO edition (Umbraco.Community.SchemeWeaver.Seo): the curated
 * content kinds, the kind-based set-up of content types, the site identity, per-page overrides
 * and the site health check. They write ordinary SchemeWeaver mappings, so the core tools
 * (get-schema-mapping, preview-json-ld, validate-mapping…) see the same data.
 */

import { ToolCollectionExport } from "@umbraco-cms/mcp-server-sdk";
import getSeoContentKindsTool from "./get/get-seo-content-kinds.js";
import getSeoContentTypeStatusTool from "./get/get-seo-content-type-status.js";
import setupSeoContentTypeTool from "./post/setup-seo-content-type.js";
import saveSeoMappingTool from "./put/save-seo-mapping.js";
import getSiteIdentityTool from "./get/get-site-identity.js";
import setSiteIdentityTool from "./put/set-site-identity.js";
import getSeoPageSummaryTool from "./get/get-seo-page-summary.js";
import setSeoPageOverrideTool from "./put/set-seo-page-override.js";
import clearSeoPageOverrideTool from "./delete/clear-seo-page-override.js";
import runSeoHealthCheckTool from "./post/run-seo-health-check.js";

const collection: ToolCollectionExport = {
  metadata: {
    name: "schemeweaver-seo",
    displayName: "SchemeWeaver SEO",
    description:
      "The SchemeWeaver SEO edition: curated Google-rewarded content kinds, kind-based set-up of content types, " +
      "the site identity (publisher), per-page type overrides/exclusions and a site health check. " +
      "Typical flow: get-seo-content-type-status to find page types that are not set up, get-seo-content-kinds to " +
      "pick a kind, setup-seo-content-type for pre-filled slots, save-seo-mapping, then run-seo-health-check. " +
      "Requires the Seo satellite package; its endpoints answer 403/404 without it.",
  },
  tools: () => [
    getSeoContentKindsTool,
    getSeoContentTypeStatusTool,
    setupSeoContentTypeTool,
    saveSeoMappingTool,
    getSiteIdentityTool,
    setSiteIdentityTool,
    getSeoPageSummaryTool,
    setSeoPageOverrideTool,
    clearSeoPageOverrideTool,
    runSeoHealthCheckTool,
  ],
};

export default collection;
