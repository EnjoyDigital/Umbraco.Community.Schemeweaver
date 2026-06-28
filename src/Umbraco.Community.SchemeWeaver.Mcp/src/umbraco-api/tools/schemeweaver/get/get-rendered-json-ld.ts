/**
 * Get Rendered JSON-LD Tool
 *
 * direct Delivery-API fetch; intentionally bypasses the management client.
 *
 * The SchemeWeaver Delivery API (/umbraco/delivery/api/v2/schemeweaver/...) is
 * anonymous and NOT part of the management OpenAPI surface, so there is no
 * Orval-generated client for it. We therefore use a plain fetch here rather
 * than executeGetApiCall/UmbracoManagementClient. This is the *live* render a
 * public visitor's page would emit — ground truth — as opposed to the
 * backoffice-context preview produced by preview-json-ld.
 *
 * TLS note: when run as a Claude Code plugin only the three UMBRACO_* vars are
 * passed through mcpServers.env, so NODE_TLS_REJECT_UNAUTHORIZED is NOT
 * propagated. Against a self-signed localhost host the fetch will fail TLS
 * verification unless that var is set in the environment — same limitation as
 * the existing management tools.
 */

import {
  withStandardDecorators,
  createToolResult,
  type ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import { resolveRenderHost, buildRenderedJsonLdUrl } from "../../../base-url.js";

const inputSchema = {
  route: z
    .string()
    .describe(
      "Site-relative route to render, e.g. '/' or '/blog/my-post'. This is the live, public Delivery-API render " +
        "(ground truth) — NOT a backoffice preview. Use this to confirm what structured data a real page actually emits."
    ),
  scope: z
    .enum(["site", "page", "all"])
    .optional()
    .describe(
      "Which JSON-LD blocks to return: 'page' (this node only), 'site' (site-level), or 'all'. Omit for the endpoint default."
    ),
  culture: z
    .string()
    .optional()
    .describe("Optional culture code (e.g. 'en-US', 'de-DE') for language-variant content"),
  host: z
    .string()
    .optional()
    .describe(
      "Optional host override (scheme + authority, e.g. 'https://www.example.com'). When set, this overrides the " +
        "configured base URL so you can fetch the LIVE render from the real public site instead of the local " +
        "sandbox/TestHost — letting you check ground truth against production without re-pointing UMBRACO_BASE_URL. " +
        "Trailing slashes are stripped. When omitted, the configured UMBRACO_BASE_URL (or the TestHost default) is used."
    ),
};

const outputSchema = z.object({
  requestUrl: z.string(),
  httpStatus: z.number(),
  // Body shape is opaque to this tool: the endpoint returns an OBJECT
  // { "schemaOrg": [ ...json-ld blocks... ] }, not a bare array.
  jsonLd: z.unknown(),
  note: z.string(),
});

const getRenderedJsonLdTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "get-rendered-json-ld",
  description:
    "Fetches the LIVE JSON-LD a published page actually emits, straight from SchemeWeaver's anonymous Delivery API " +
    "(/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route). This is the authoritative ground truth for verifying " +
    "structured data — distinct from preview-json-ld, which renders in the backoffice/management context and can " +
    "resolve URLs (@id) differently. The response always surfaces requestUrl and httpStatus (even on 404/401/empty) " +
    "and a 'note' explaining the result, including the case where HTTP 200 returns ZERO JSON-LD blocks. " +
    "Complements preview-json-ld: use that for the in-progress backoffice render and this for the live, public ground truth. " +
    "Pass the optional 'host' to fetch from the real public site (e.g. 'https://www.example.com') instead of the configured " +
    "base URL, without re-pointing UMBRACO_BASE_URL. " +
    "The Delivery API is OFF by default in Umbraco; an Api-Key may be required (UMBRACO_DELIVERY_API_KEY).",
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async ({ route, scope, culture, host }) => {
    const base = resolveRenderHost(host);
    const requestUrl = buildRenderedJsonLdUrl({ base, route, scope, culture });

    const headers: Record<string, string> = {};
    const apiKey = process.env.UMBRACO_DELIVERY_API_KEY;
    if (apiKey) {
      headers["Api-Key"] = apiKey;
    }

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 10_000);

    try {
      const res = await fetch(requestUrl, {
        method: "GET",
        headers,
        signal: controller.signal,
      });

      const rawText = await res.text();
      let parsed: unknown;
      try {
        parsed = JSON.parse(rawText);
      } catch {
        parsed = rawText;
      }

      const schemaOrg = (parsed as { schemaOrg?: unknown })?.schemaOrg;
      const hasBlocks = Array.isArray(schemaOrg) && schemaOrg.length > 0;

      let note: string;
      if (res.ok && hasBlocks) {
        note =
          "Live Delivery-API render (ground truth). Distinct from preview-json-ld (backoffice context).";
      } else if (res.ok) {
        note =
          "HTTP 200 but ZERO JSON-LD blocks — this page/scope has no mapping or nothing resolved. " +
          "This is NOT proof the page renders structured data.";
      } else if (res.status === 401) {
        note =
          "Delivery API requires an Api-Key, or PublicAccess is disabled (DeliveryApi:PublicAccess). " +
          "The Delivery API is OFF by default in Umbraco — this may not be about the route.";
      } else if (res.status === 404) {
        note =
          "EITHER the route did not resolve to a published node OR the Delivery API is not enabled (it is off by default).";
      } else {
        note = `HTTP ${res.status} ${res.statusText}. Body: ${rawText.slice(0, 500)}`;
      }

      return createToolResult({
        requestUrl,
        httpStatus: res.status,
        jsonLd: parsed,
        note,
      });
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return createToolResult({
        requestUrl,
        httpStatus: 0,
        jsonLd: [],
        note:
          `Network failure calling ${requestUrl}: ${message}. ` +
          "Likely connection refused, DNS failure, or TLS rejection. Against a self-signed localhost host, " +
          "set NODE_TLS_REJECT_UNAUTHORIZED=0 in the environment (note: as a Claude Code plugin only the " +
          "UMBRACO_* vars are forwarded, so this var may not propagate — same limitation as the management tools).",
      });
    } finally {
      clearTimeout(timeout);
    }
  },
};

export default withStandardDecorators(getRenderedJsonLdTool);
