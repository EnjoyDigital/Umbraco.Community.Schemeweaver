/**
 * Shared base-URL helpers.
 *
 * The configured Umbraco base URL is the single source of truth for where the
 * MCP server talks to (management API, Delivery API) and for the {siteUrl}/
 * absolute-URL tokens that JSON-LD @id resolution derives from. Centralising
 * the read keeps index.ts, get-server-info and get-rendered-json-ld consistent.
 */

export const DEFAULT_BASE_URL = "https://localhost:44308";

/**
 * Resolves the configured Umbraco base URL from UMBRACO_BASE_URL, falling back
 * to the TestHost default, with any trailing slashes stripped so callers can
 * safely concatenate paths.
 */
export function resolveBaseUrl(): string {
  return (process.env.UMBRACO_BASE_URL || DEFAULT_BASE_URL).replace(/\/+$/, "");
}

export interface RenderedJsonLdUrlOptions {
  /** Base URL (already trailing-slash-stripped, e.g. from resolveBaseUrl()). */
  base: string;
  /** Site-relative route, e.g. '/' or '/blog/my-post'. */
  route: string;
  /** Optional Delivery-API scope: site | page | all. */
  scope?: string;
  /** Optional culture code, e.g. 'en-US'. */
  culture?: string;
}

/**
 * Pure builder for the SchemeWeaver Delivery-API "by-route" URL. Kept free of
 * fetch/IO so URL and query-string construction is unit-testable in isolation.
 * scope/culture are only appended when defined.
 */
export function buildRenderedJsonLdUrl({
  base,
  route,
  scope,
  culture,
}: RenderedJsonLdUrlOptions): string {
  const qs = new URLSearchParams({ route });
  if (scope !== undefined) {
    qs.set("scope", scope);
  }
  if (culture !== undefined) {
    qs.set("culture", culture);
  }
  return `${base}/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route?${qs}`;
}
