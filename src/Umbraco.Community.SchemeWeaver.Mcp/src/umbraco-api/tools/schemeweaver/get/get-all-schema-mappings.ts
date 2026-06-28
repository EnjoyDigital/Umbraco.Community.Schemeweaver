/**
 * Get All Schema Mappings Tool
 */

import {
  withStandardDecorators,
  executeGetItemsApiCall,
  CAPTURE_RAW_HTTP_RESPONSE,
  ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import { getSchemeweaverMappingsResponseItem } from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

// Forward-compatible: the list view enriches each mapping with `reachability`
// (routed-page | composed-from-block | unknown). The per-property range
// `warnings` are intentionally NOT computed here (bounded out to keep a
// many-mapping listing cheap) — fetch a single mapping with get-schema-mapping
// for those. .extend() so the generated zod item does not strip the field
// before Orval is regenerated; optional → inert when absent.
// NOTE (leader): drop this shim once `npm run generate` folds reachability in.
const outputSchema = z.object({
  items: z.array(
    getSchemeweaverMappingsResponseItem.extend({
      reachability: z.string().optional(),
      // Disk/DB drift vs the mapping's uSync .config: in-sync | db-only | disk-only |
      // content-differs | usync-unavailable (when the uSync addon isn't installed).
      driftStatus: z.string().optional(),
    })
  ),
});

const getAllSchemaMappingsTool: ToolDefinition<undefined, typeof outputSchema> = {
  name: "get-all-schema-mappings",
  description:
    "Lists every SchemeWeaver mapping in the site: which Umbraco content type maps to which Schema.org type, " +
    "whether it is enabled and inherited by descendant content, and all of its property mappings. Each mapping also " +
    "carries `reachability` (routed-page emits on its own URL; composed-from-block only emits inside a containing page's " +
    "block mapping) so you can spot mappings that will never emit on their own, plus `driftStatus` (whether each mapping " +
    "matches its committed uSync .config on disk — in-sync/db-only/disk-only/content-differs/usync-unavailable). " +
    "Useful for auditing existing structured-data coverage and for copying patterns from mappings that already work.",
  outputSchema,
  slices: ["list"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async () => {
    return executeGetItemsApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.getSchemeweaverMappings(CAPTURE_RAW_HTTP_RESPONSE)
    );
  },
};

export default withStandardDecorators(getAllSchemaMappingsTool);
