/**
 * Generate Content Type Tool
 */

import {
  withStandardDecorators,
  executeGetApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  schemaTypeName: z.string().describe("Schema.org type to scaffold from, e.g. 'Recipe'"),
  documentTypeName: z.string().describe("Display name for the new Umbraco document type, e.g. 'Recipe Page'"),
  documentTypeAlias: z.string().describe("Alias for the new document type (camelCase), e.g. 'recipePage'"),
  selectedProperties: z
    .array(z.string())
    .describe(
      "Schema.org property names to create Umbraco properties for — choose from get-schema-type-properties " +
        "(ranked=true helps pick the ones that matter). Appropriate property editors are inferred per property."
    ),
  propertyGroupName: z
    .string()
    .optional()
    .default("Content")
    .describe("Tab/group name the new properties are placed under (default 'Content')"),
};

const outputSchema = z.object({
  key: z.string().describe("GUID key of the created document type"),
});

const generateContentTypeTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "generate-content-type",
  description:
    "Creates a brand-new Umbraco document type scaffolded from a Schema.org type, with properties (and sensible " +
    "property editors) for the selected schema properties, plus a SchemeWeaver mapping wired up automatically. " +
    "Use this for greenfield modelling — when content should be structured around a schema from the start. " +
    "For existing content types, use save-schema-mapping instead.",
  inputSchema,
  outputSchema,
  slices: ["create"],
  annotations: {
    destructiveHint: false,
    idempotentHint: false,
  },
  handler: async ({ schemaTypeName, documentTypeName, documentTypeAlias, selectedProperties, propertyGroupName }) => {
    return executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.postSchemeweaverGenerateContentType(
        {
          schemaTypeName,
          documentTypeName,
          documentTypeAlias,
          selectedProperties,
          propertyGroupName: propertyGroupName ?? "Content",
        },
        CAPTURE_RAW_HTTP_RESPONSE
      )
    );
  },
};

export default withStandardDecorators(generateContentTypeTool);
