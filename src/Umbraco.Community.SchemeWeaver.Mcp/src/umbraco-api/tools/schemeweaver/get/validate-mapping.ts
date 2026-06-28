/**
 * Validate Mapping Tool
 *
 * The proactive "doctor" form of the advisories that get-schema-mapping /
 * save-schema-mapping already return inline. It fetches one content type's
 * mapping and aggregates everything actionable about it — range-drop warnings,
 * missing-transform / position suggestions, drift, and reachability — into a
 * single severity-ranked checklist an author can clear top-to-bottom. Read-only:
 * it never mutates the mapping.
 */

import {
  withStandardDecorators,
  createToolResult,
  getApiClient,
  CAPTURE_RAW_HTTP_RESPONSE,
  type HttpResponse,
  type ToolDefinition,
} from "@umbraco-cms/mcp-server-sdk";
import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";
import { getSchemeweaverMappingsByContentTypeAliasResponse } from "../../../api/generated/schemeWeaverApi.zod.js";

type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

const inputSchema = {
  contentTypeAlias: z
    .string()
    .describe("The Umbraco content type alias whose SchemeWeaver mapping to validate."),
};

const checklistItemSchema = z.object({
  severity: z
    .string()
    .describe("critical | warning | suggestion | info — items are ranked in this order."),
  path: z.string().describe("Schema.org property path the item relates to ('' when not property-specific)."),
  message: z.string().describe("Human-readable description of what to check or clear."),
});

export const outputSchema = z.object({
  contentTypeAlias: z.string(),
  schemaTypeName: z.string().describe("The mapped Schema.org type ('' when no mapping is configured)."),
  allClear: z
    .boolean()
    .describe(
      "True when the checklist contains no critical/warning/suggestion items (info-only or empty) — " +
        "the mapping passes the v3 litmus test."
    ),
  checklist: z.array(checklistItemSchema),
});

// ---------------------------------------------------------------------------
// Pure aggregation logic (exported for host-free unit tests).
// ---------------------------------------------------------------------------

type Warning = { severity: string; schemaType: string; path: string; message: string };
type Mapping = {
  contentTypeAlias: string;
  schemaTypeName: string;
  reachability?: string | null;
  driftStatus?: string | null;
  warnings?: Warning[];
};
type ChecklistItem = { severity: string; path: string; message: string };

// critical > warning > suggestion > info; unknown severities sort last, stably.
const SEVERITY_RANK: Record<string, number> = {
  critical: 0,
  warning: 1,
  suggestion: 2,
  info: 3,
};

function rankOf(severity: string): number {
  return SEVERITY_RANK[severity.toLowerCase()] ?? 4;
}

/** Stable sort by severity rank (critical first, info last). */
function rankChecklist(items: ChecklistItem[]): ChecklistItem[] {
  return items
    .map((item, index) => ({ item, index }))
    .sort((a, b) => rankOf(a.item.severity) - rankOf(b.item.severity) || a.index - b.index)
    .map(({ item }) => item);
}

/**
 * Build the ranked checklist for a single mapping. Carries every `warnings[]`
 * item through with its own severity, then synthesises reachability/drift items
 * ONLY when they are not already represented in the warnings (avoids duplicates).
 */
export function buildValidationChecklist(mapping: Mapping): z.infer<typeof outputSchema> {
  const warnings = mapping.warnings ?? [];
  const checklist: ChecklistItem[] = warnings.map((w) => ({
    severity: w.severity,
    path: w.path,
    message: w.message,
  }));

  // composed-from-block reachability: informational note that this type only
  // emits inside a containing page's block mapping (it never emits on its own
  // URL). Info severity so it does not, by itself, fail the all-clear litmus.
  const reachabilityAlreadyNoted = warnings.some(
    (w) => w.path?.toLowerCase() === "reachability" || /composed-from-block|block mapping/i.test(w.message)
  );
  if (mapping.reachability === "composed-from-block" && !reachabilityAlreadyNoted) {
    checklist.push({
      severity: "info",
      path: "reachability",
      message:
        "This type is composed-from-block: it only emits JSON-LD inside a containing page's block mapping, " +
        "never on its own URL. This is expected for block element types and is informational, not a defect.",
    });
  }

  // Drift: a db-only or content-differs mapping is not reproducible from
  // config-as-code until exported. Suggestion severity so it shows as an item
  // to clear.
  const driftAlreadyNoted = warnings.some(
    (w) => w.path?.toLowerCase() === "driftstatus" || /\bdrift\b|usync|export to/i.test(w.message)
  );
  const drift = mapping.driftStatus;
  if ((drift === "db-only" || drift === "content-differs") && !driftAlreadyNoted) {
    checklist.push({
      severity: "suggestion",
      path: "driftStatus",
      message:
        drift === "db-only"
          ? "This mapping is saved in the database but has never been exported to uSync (db-only). " +
            "Run export-mappings-to-usync to make it reproducible from config-as-code."
          : "This mapping differs from its committed uSync .config on disk (content-differs). " +
            "Run export-mappings-to-usync to bring the .config back in sync.",
    });
  }

  const ranked = rankChecklist(checklist);
  const allClear = !ranked.some((item) => rankOf(item.severity) <= 2);

  return {
    contentTypeAlias: mapping.contentTypeAlias,
    schemaTypeName: mapping.schemaTypeName,
    allClear,
    checklist: ranked,
  };
}

/** The checklist returned when no mapping exists for the requested alias. */
export function noMappingResult(contentTypeAlias: string): z.infer<typeof outputSchema> {
  return {
    contentTypeAlias,
    schemaTypeName: "",
    allClear: false,
    checklist: [
      {
        severity: "warning",
        path: "",
        message:
          `No SchemeWeaver mapping is configured for content type '${contentTypeAlias}'. ` +
          "Create one with save-schema-mapping (or generate-content-type) before this type can emit JSON-LD.",
      },
    ],
  };
}

const validateMappingTool: ToolDefinition<typeof inputSchema, typeof outputSchema> = {
  name: "validate-mapping",
  description:
    "Validates one content type's SchemeWeaver mapping and returns a single, severity-ranked checklist an author can " +
    "clear top-to-bottom — the proactive 'doctor' form of the advisories get-schema-mapping returns inline. It aggregates: " +
    "range-drop warnings (properties mapped outside their Schema.org range that would be silently dropped), missing-transform " +
    "and position suggestions the engine now emits, drift (a db-only or content-differs mapping that should be exported to " +
    "uSync), and reachability (a composed-from-block type only emits inside a containing page's block mapping). Items are ranked " +
    "critical > warning > suggestion > info. `allClear` is true when nothing critical/warning/suggestion remains — i.e. the " +
    "mapping passes the v3 litmus test (info-only notes do not fail it). Returns a clear allClear:false item when no mapping " +
    "exists for the alias. This tool is READ-ONLY; it does not mutate the mapping.",
  inputSchema,
  outputSchema,
  slices: ["read"],
  annotations: {
    readOnlyHint: true,
  },
  handler: async ({ contentTypeAlias }) => {
    const client = getApiClient<SchemeWeaverApiClient>();
    const response = (await client.getSchemeweaverMappingsByContentTypeAlias(
      contentTypeAlias,
      CAPTURE_RAW_HTTP_RESPONSE
    )) as unknown as HttpResponse<unknown>;

    // Only a genuine 404 means "no mapping configured". An auth (401/403) or server (5xx) error
    // must NOT be reported as a missing mapping — that would send the caller to create one that may
    // already exist. Surface it as a distinct critical item instead.
    if (response.status === 404) {
      return createToolResult(noMappingResult(contentTypeAlias));
    }

    if (response.status >= 400) {
      return createToolResult({
        contentTypeAlias,
        schemaTypeName: "",
        allClear: false,
        checklist: [
          {
            severity: "critical",
            path: "(request)",
            message:
              `Could not load the mapping (HTTP ${response.status}) — this is NOT a 'no mapping' result. ` +
              (response.status === 401 || response.status === 403
                ? "The request was unauthorized; check the API user credentials before assuming the mapping is missing."
                : "The server returned an error; retry or check the server logs."),
          },
        ],
      });
    }

    const mapping = getSchemeweaverMappingsByContentTypeAliasResponse.parse(response.data);
    return createToolResult(buildValidationChecklist(mapping));
  },
};

export default withStandardDecorators(validateMappingTool);
