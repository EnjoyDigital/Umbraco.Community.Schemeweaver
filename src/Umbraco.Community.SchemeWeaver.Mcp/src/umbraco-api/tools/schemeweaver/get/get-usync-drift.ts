/**
 * Get uSync Drift Tool
 *
 * Reports drift between the schema mappings stored in the database and their on-disk uSync
 * `.config` files. Computed server-side (the MCP cannot read the server's file system) and
 * returned over the management API. The two new endpoints are called via the raw
 * UmbracoManagementClient transport (like get-server-info) rather than the Orval client.
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

interface DriftReport {
  usyncAvailable: boolean;
  items: { contentTypeAlias: string; status: string }[];
}

const outputSchema = z.object({
  usyncAvailable: z
    .boolean()
    .describe("False when the Umbraco.Community.SchemeWeaver.uSync addon is not installed — drift cannot be computed."),
  items: z.array(
    z.object({
      contentTypeAlias: z.string(),
      status: z
        .string()
        .describe("in-sync | db-only | disk-only | content-differs | usync-unavailable"),
    })
  ),
});

const getUsyncDriftTool: ToolDefinition<undefined, typeof outputSchema> = {
  name: "get-usync-drift",
  description:
    "Reports disk/DB drift for every SchemeWeaver mapping: whether each mapping in the database matches its committed " +
    "uSync .config on disk. Statuses: in-sync (DB == disk), db-only (saved but never exported), disk-only (a committed " +
    ".config with no DB mapping), content-differs (both exist but differ), usync-unavailable (the uSync addon isn't " +
    "installed). Use this to check config-as-code reproducibility before committing; pair with export-mappings-to-usync " +
    "to write DB mappings out to disk.",
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async () => {
    const response = (await UmbracoManagementClient<DriftReport>(
      { url: "/umbraco/management/api/v1/schemeweaver/mappings/drift", method: "GET" },
      CAPTURE_RAW_HTTP_RESPONSE,
    )) as unknown as HttpResponse<DriftReport>;

    return createToolResult(response.data);
  },
};

export default withStandardDecorators(getUsyncDriftTool);
