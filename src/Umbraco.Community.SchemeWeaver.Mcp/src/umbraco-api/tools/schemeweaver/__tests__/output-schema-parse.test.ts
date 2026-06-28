/**
 * P3.1 parse-survival guard (host-free).
 *
 * After removing the forward-compat `.extend()` shims, each tool's outputSchema is the
 * generated zod schema directly. These tests assert that a representative C# response still
 * survives `outputSchema.parse()` with the previously-shimmed fields intact — so if a future
 * regen ever drops a field from the generated schema, this goes red instead of silently
 * stripping data at runtime. No live host required.
 */

import { outputSchema as saveOutputSchema } from "../post/save-schema-mapping.js";
import { outputSchema as getOutputSchema } from "../get/get-schema-mapping.js";
import { outputSchema as getAllOutputSchema } from "../get/get-all-schema-mappings.js";
import { outputSchema as previewOutputSchema } from "../post/preview-json-ld.js";

const sampleMapping = {
  contentTypeAlias: "faqPage",
  contentTypeKey: "550e8400-e29b-41d4-a716-446655440000",
  schemaTypeName: "FAQPage",
  isEnabled: true,
  isInherited: false,
  idOverride: null,
  propertyMappings: [],
  reachability: "routed-page",
  warnings: [{ severity: "warning", schemaType: "FAQPage", path: "hasPart", message: "out of range" }],
  driftStatus: "in-sync",
  persistedTo: null,
};

const samplePreview = {
  jsonLd: "{}",
  isValid: true,
  errors: [],
  issues: [],
  context: "backoffice-preview",
  resolvedBaseUrl: null,
};

describe("P3.1 output schema parse-survival", () => {
  it("save-schema-mapping keeps reachability/warnings/driftStatus/persistedTo", () => {
    const parsed = saveOutputSchema.parse(sampleMapping) as typeof sampleMapping;
    expect(parsed.reachability).toBe("routed-page");
    expect(parsed.driftStatus).toBe("in-sync");
    expect(parsed.persistedTo).toBeNull();
    expect(parsed.warnings).toHaveLength(1);
    expect(parsed.warnings[0].path).toBe("hasPart");
  });

  it("get-schema-mapping keeps reachability/warnings/driftStatus", () => {
    const parsed = getOutputSchema.parse(sampleMapping) as typeof sampleMapping;
    expect(parsed.reachability).toBe("routed-page");
    expect(parsed.driftStatus).toBe("in-sync");
    expect(parsed.warnings).toHaveLength(1);
  });

  it("get-all-schema-mappings keeps per-item reachability/driftStatus", () => {
    const parsed = getAllOutputSchema.parse({ items: [sampleMapping] }) as { items: (typeof sampleMapping)[] };
    expect(parsed.items[0].reachability).toBe("routed-page");
    expect(parsed.items[0].driftStatus).toBe("in-sync");
  });

  it("preview-json-ld keeps context and resolvedBaseUrl", () => {
    const parsed = previewOutputSchema.parse(samplePreview) as typeof samplePreview;
    expect(parsed.context).toBe("backoffice-preview");
    expect(parsed.resolvedBaseUrl).toBeNull();
  });

  it("preview-json-ld requires context (regression guard for the removed shim)", () => {
    const { context, ...withoutContext } = samplePreview;
    expect(() => previewOutputSchema.parse(withoutContext)).toThrow();
  });
});
