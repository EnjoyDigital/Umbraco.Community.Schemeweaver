/**
 * Run SEO Health Check Tool — one preview per set-up content type, aggregated.
 */

import { withStandardDecorators, executeGetApiCall, CAPTURE_RAW_HTTP_RESPONSE, ToolDefinition } from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import { healthReportSchema, SEO_NOTE, type SchemeWeaverApiClient } from "../shared.js";

const inputSchema = {
  force: z.boolean().default(false).describe("True to bypass the server-side cache (default five minutes) and re-render now."),
};

export const outputSchema = healthReportSchema;

const tool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "run-seo-health-check",
  description:
    "Renders the most recently published page of every set-up content type through SchemeWeaver's Google Rich Results " +
    "validator and aggregates the findings: per type the sample page, critical and warning counts and the issues " +
    "(path + message), plus site totals. `cached` tells you whether the report was served from the server cache " +
    "(`generatedAt` says when). The fastest way to answer 'what is wrong with this site's structured data?' before " +
    "drilling into validate-mapping or get-seo-page-summary. Read-only despite being a POST. " +
    SEO_NOTE,
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: { readOnlyHint: true },
  handler: async ({ force }) =>
    executeGetApiCall<unknown, SchemeWeaverApiClient>((client) =>
      client.postSchemeweaverSeoHealthCheck({ force: force ?? false }, CAPTURE_RAW_HTTP_RESPONSE)
    ),
};

export default withStandardDecorators(tool);
