/**
 * Get Umbraco Content Type Properties Tool
 */

import {
  withStandardDecorators,
  executeGetItemsApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  alias: z.string().describe("Umbraco content type alias, e.g. 'blogPost'"),
};

// The endpoint returns an anonymous projection, so the shape is declared here
// rather than generated (see SchemeWeaverApiController.GetContentTypeProperties).
const outputSchema = z.object({
  items: z.array(
    z.object({
      alias: z.string(),
      name: z.string().nullish(),
      editorAlias: z.string(),
      description: z.string().nullish(),
      valueSchema: z
        .string()
        .nullish()
        .describe(
          "The property's value JSON Schema (Umbraco 17.4+) as a stringified JSON object — the actual shape the " +
            "stored value takes (e.g. {type:'string',maxLength:250} for a textbox; UUID/crop structure for a media " +
            "picker; numeric ranges). Use it to map precisely beyond the editorAlias. Null on Umbraco < 17.4 or for " +
            "built-in/no-schema properties."
        ),
    })
  ),
});

const getContentTypePropertiesTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "get-content-type-properties",
  description:
    "Gets the properties of an Umbraco content type: alias, name, editorAlias (the property editor, e.g. Umbraco.TextBox, " +
    "Umbraco.RichText, Umbraco.MediaPicker3, Umbraco.BlockList, Umbraco.DateTime) and description. " +
    "Built-in node properties ('__name', '__url', '__createDate', '__updateDate') are included alongside the editor-defined ones. " +
    "The editorAlias is a strong signal for mapping decisions: media pickers suit image/logo, " +
    "date pickers suit datePublished/dateModified, block lists suit blockContent mappings, " +
    "content pickers suit parent/ancestor/sibling or nested-object mappings. " +
    "On Umbraco 17.4+ each property also carries `valueSchema` — the JSON Schema of the actual stored value " +
    "(types, maxLength, UUID/crop structure, ranges) — a stronger signal than the editor alias for choosing the " +
    "right Schema.org property and transforms.",
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async ({ alias }) => {
    return executeGetItemsApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.getSchemeweaverContentTypesByAliasProperties(alias, CAPTURE_RAW_HTTP_RESPONSE)
    );
  },
};

export default withStandardDecorators(getContentTypePropertiesTool);
