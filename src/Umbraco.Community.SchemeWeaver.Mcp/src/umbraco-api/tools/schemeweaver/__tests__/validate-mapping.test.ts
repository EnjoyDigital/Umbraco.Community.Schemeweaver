/**
 * validate-mapping aggregation logic (host-free).
 *
 * The TestHost is not required: these tests drive the pure `buildValidationChecklist`
 * / `noMappingResult` helpers and assert the outputSchema accepts their results.
 * The integration-shape (live TestHost) variant belongs in schemeweaver-tools.test.ts;
 * this file guards the ranking, dedup, drift/reachability synthesis and all-clear
 * semantics without a live host.
 */

import {
  buildValidationChecklist,
  noMappingResult,
  outputSchema,
} from "../get/validate-mapping.js";

const baseMapping = {
  contentTypeAlias: "blogPost",
  contentTypeKey: "550e8400-e29b-41d4-a716-446655440000",
  schemaTypeName: "BlogPosting",
  isEnabled: true,
  isInherited: false,
  idOverride: null,
  propertyMappings: [],
  reachability: "routed-page",
  warnings: [] as Array<{ severity: string; schemaType: string; path: string; message: string }>,
  driftStatus: "in-sync",
  persistedTo: null,
};

describe("validate-mapping aggregation", () => {
  it("is all-clear with no warnings, routed-page reachability and in-sync drift", () => {
    const result = buildValidationChecklist(baseMapping);
    expect(result.allClear).toBe(true);
    expect(result.checklist).toHaveLength(0);
    expect(result.schemaTypeName).toBe("BlogPosting");
    expect(() => outputSchema.parse(result)).not.toThrow();
  });

  it("ranks critical > warning > suggestion > info, stable within a rank", () => {
    const result = buildValidationChecklist({
      ...baseMapping,
      reachability: "composed-from-block", // synthesises an info item
      driftStatus: "db-only", // synthesises a suggestion item
      warnings: [
        { severity: "info", schemaType: "BlogPosting", path: "a", message: "info A" },
        { severity: "warning", schemaType: "BlogPosting", path: "b", message: "warn B" },
        { severity: "critical", schemaType: "BlogPosting", path: "c", message: "crit C" },
        { severity: "warning", schemaType: "BlogPosting", path: "d", message: "warn D" },
      ],
    });

    const severities = result.checklist.map((i) => i.severity);
    expect(severities).toEqual([
      "critical",
      "warning",
      "warning",
      "suggestion",
      "info",
      "info",
    ]);
    // Stable within the warning rank: B before D (input order preserved).
    const warnPaths = result.checklist.filter((i) => i.severity === "warning").map((i) => i.path);
    expect(warnPaths).toEqual(["b", "d"]);
    expect(result.allClear).toBe(false);
    expect(() => outputSchema.parse(result)).not.toThrow();
  });

  it("synthesises a suggestion for db-only drift and is therefore not all-clear", () => {
    const result = buildValidationChecklist({ ...baseMapping, driftStatus: "db-only" });
    expect(result.allClear).toBe(false);
    const drift = result.checklist.find((i) => i.path === "driftStatus");
    expect(drift?.severity).toBe("suggestion");
    expect(drift?.message).toMatch(/export-mappings-to-usync/);
  });

  it("synthesises a suggestion for content-differs drift", () => {
    const result = buildValidationChecklist({ ...baseMapping, driftStatus: "content-differs" });
    const drift = result.checklist.find((i) => i.path === "driftStatus");
    expect(drift?.severity).toBe("suggestion");
    expect(drift?.message).toMatch(/content-differs/);
  });

  it("does NOT synthesise drift for in-sync/disk-only/usync-unavailable", () => {
    for (const driftStatus of ["in-sync", "disk-only", "usync-unavailable", null]) {
      const result = buildValidationChecklist({ ...baseMapping, driftStatus });
      expect(result.checklist.find((i) => i.path === "driftStatus")).toBeUndefined();
    }
  });

  it("synthesises an info note for composed-from-block reachability (does not fail all-clear)", () => {
    const result = buildValidationChecklist({ ...baseMapping, reachability: "composed-from-block" });
    const reach = result.checklist.find((i) => i.path === "reachability");
    expect(reach?.severity).toBe("info");
    // Info-only → still all-clear.
    expect(result.allClear).toBe(true);
  });

  it("does not duplicate reachability when a warning already represents it", () => {
    const result = buildValidationChecklist({
      ...baseMapping,
      reachability: "composed-from-block",
      warnings: [
        {
          severity: "info",
          schemaType: "BlogPosting",
          path: "reachability",
          message: "already composed-from-block",
        },
      ],
    });
    expect(result.checklist.filter((i) => i.path === "reachability")).toHaveLength(1);
  });

  it("does not duplicate drift when a warning already mentions it", () => {
    const result = buildValidationChecklist({
      ...baseMapping,
      driftStatus: "db-only",
      warnings: [
        {
          severity: "suggestion",
          schemaType: "BlogPosting",
          path: "driftStatus",
          message: "drift: export to uSync",
        },
      ],
    });
    expect(result.checklist.filter((i) => i.path === "driftStatus")).toHaveLength(1);
  });

  it("treats the new 'suggestion' severity as a litmus-failing item", () => {
    const result = buildValidationChecklist({
      ...baseMapping,
      warnings: [
        { severity: "suggestion", schemaType: "BlogPosting", path: "datePublished", message: "add a transform" },
      ],
    });
    expect(result.allClear).toBe(false);
    expect(result.checklist[0].severity).toBe("suggestion");
  });

  it("returns a clear, not-all-clear single item when no mapping exists", () => {
    const result = noMappingResult("orphanType");
    expect(result.allClear).toBe(false);
    expect(result.schemaTypeName).toBe("");
    expect(result.checklist).toHaveLength(1);
    expect(result.checklist[0].message).toMatch(/No SchemeWeaver mapping is configured/);
    expect(() => outputSchema.parse(result)).not.toThrow();
  });
});
