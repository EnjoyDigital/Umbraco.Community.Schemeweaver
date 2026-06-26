/**
 * SchemeWeaver Tool Collection
 *
 * Tools for mapping Umbraco content types to Schema.org types and generating
 * JSON-LD structured data via the SchemeWeaver management API.
 */

import { ToolCollectionExport } from "@umbraco-cms/mcp-server-sdk";
import searchSchemaTypesTool from "./get/search-schema-types.js";
import getSchemaTypePropertiesTool from "./get/get-schema-type-properties.js";
import listContentTypesTool from "./get/list-content-types.js";
import getContentTypePropertiesTool from "./get/get-content-type-properties.js";
import getBlockElementTypesTool from "./get/get-block-element-types.js";
import getAllSchemaMappingsTool from "./get/get-all-schema-mappings.js";
import getSchemaMappingTool from "./get/get-schema-mapping.js";
import getRenderedJsonLdTool from "./get/get-rendered-json-ld.js";
import suggestPropertyMappingsTool from "./post/suggest-property-mappings.js";
import saveSchemaMappingTool from "./post/save-schema-mapping.js";
import previewJsonLdTool from "./post/preview-json-ld.js";
import generateContentTypeTool from "./post/generate-content-type.js";
import deleteSchemaMappingTool from "./delete/delete-schema-mapping.js";

const collection: ToolCollectionExport = {
  metadata: {
    name: "schemeweaver",
    displayName: "SchemeWeaver",
    description:
      "Map Umbraco content types to Schema.org types and generate JSON-LD structured data. " +
      "Typical mapping workflow: search-schema-types to pick the most specific type, " +
      "get-content-type-properties + get-schema-type-properties (ranked=true) to understand both sides, " +
      "suggest-property-mappings for the heuristic baseline, then reason semantically and persist an improved " +
      "mapping with save-schema-mapping, and finally preview-json-ld to validate and iterate.",
  },
  tools: () => [
    // Discovery (read)
    searchSchemaTypesTool,
    getSchemaTypePropertiesTool,
    listContentTypesTool,
    getContentTypePropertiesTool,
    getBlockElementTypesTool,
    // Mappings (read)
    getAllSchemaMappingsTool,
    getSchemaMappingTool,
    suggestPropertyMappingsTool,
    // Mappings (write)
    saveSchemaMappingTool,
    deleteSchemaMappingTool,
    // Verification
    previewJsonLdTool,
    // Live Delivery-API render (ground truth, bypasses the management client)
    getRenderedJsonLdTool,
    // Scaffolding
    generateContentTypeTool,
  ],
};

export default collection;
