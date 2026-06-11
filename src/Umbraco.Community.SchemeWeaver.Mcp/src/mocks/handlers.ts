/**
 * MSW Request Handlers
 *
 * Mock API handlers used by unit tests when USE_MOCK_API=true.
 * Integration tests run against a real Umbraco instance and do not use these.
 *
 * Add handlers here if you want unit tests that don't require a running
 * Umbraco instance, e.g.:
 *
 * http.get("*\/umbraco/management/api/v1/schemeweaver/schema-types", () =>
 *   HttpResponse.json([{ name: "Article", description: "...", parentTypeName: "CreativeWork", propertyCount: 10 }])
 * )
 */

import type { RequestHandler } from "msw";

export const handlers: RequestHandler[] = [];
