/**
 * Export Mappings To uSync Tool
 *
 * On-demand "write config-as-code now" primitive: serialises schema mappings from the database
 * to their uSync `.config` files, independent of the SchemeWeaver-owned ExportMappingsToUSyncOnSave
 * flag. Retires the manual hand-authoring of .config files. Calls the management API via the raw
 * UmbracoManagementClient transport.
 */

import {
  withStandardDecorators,
  createToolResult,
  UmbracoManagementClient,
  CAPTURE_RAW_HTTP_RESPONSE,
  type ToolDefinition,
  type HttpResponse,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";

interface ExportResult {
  usyncAvailable: boolean;
  folder: string | null;
  items: { alias: string; written: boolean; error?: string | null }[];
}

const inputSchema = {
  contentTypeAlias: z
    .string()
    .optional()
    .describe("Omit to export ALL mappings; set to export just one content type's mapping."),
};

const outputSchema = z.object({
  usyncAvailable: z
    .boolean()
    .describe("False when the Umbraco.Community.SchemeWeaver.uSync addon is not installed — nothing was written."),
  folder: z.string().nullable().describe("The uSync folder the .config files were written to."),
  items: z.array(
    z.object({
      alias: z.string(),
      written: z.boolean(),
      error: z.string().nullish().describe("Set when written=false, e.g. a read-only content root."),
    })
  ),
});

const exportMappingsToUsyncTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "export-mappings-to-usync",
  description:
    "Exports SchemeWeaver mappings from the database to their uSync .config files on disk so they can be committed to " +
    "source control and reproduced in other environments via a normal uSync import — no manual file authoring. Omit " +
    "contentTypeAlias to export every mapping, or pass one to export a single mapping. Per-item failures (e.g. a " +
    "read-only content root) are reported as written=false with an error rather than failing the whole call. After " +
    "exporting, get-usync-drift should report the mapping(s) as in-sync. Returns usyncAvailable=false (writing nothing) " +
    "when the uSync addon isn't installed.",
  inputSchema,
  outputSchema,
  slices: ["update"],
  annotations: {
    destructiveHint: false,
    idempotentHint: true,
  },
  handler: async ({ contentTypeAlias }) => {
    const data = contentTypeAlias ? { contentTypeAlias } : {};
    const response = (await UmbracoManagementClient<ExportResult>(
      { url: "/umbraco/management/api/v1/schemeweaver/mappings/export", method: "POST", data },
      CAPTURE_RAW_HTTP_RESPONSE,
    )) as unknown as HttpResponse<ExportResult>;

    return createToolResult(response.data);
  },
};

export default withStandardDecorators(exportMappingsToUsyncTool);
