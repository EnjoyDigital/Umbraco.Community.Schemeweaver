/**
 * Shared Zod shapes for the SchemeWeaver SEO tools.
 *
 * The SEO controller returns untyped IActionResults, so the generated Zod responses are
 * `unknown`; the shapes below describe the camelCase DTOs in
 * src/Umbraco.Community.SchemeWeaver.Seo/Models/Api/SeoApiModels.cs. They are deliberately
 * `passthrough()` so a field added server-side is never stripped at runtime.
 */

import { z } from "zod";
import type { getSchemeWeaverManagementAPI } from "../../api/generated/schemeWeaverApi.js";

export type SchemeWeaverApiClient = ReturnType<typeof getSchemeWeaverManagementAPI>;

export const slotValueKind = z
  .enum(["none", "property", "fixed", "siteIdentity", "nested", "blocks", "advanced"])
  .describe(
    "How the slot is filled: 'property' (a field on the page, set propertyAlias; built-ins __name/__url/__createDate/__updateDate allowed), " +
      "'fixed' (set fixedValue), 'siteIdentity' (the site's Organisation/Person, only for slots that declare siteIdentityPieceKey), " +
      "'nested' (fill the entity's parts: set nested = { subSchemaProperty: slotValue }), 'none' (leave empty). " +
      "'blocks' and 'advanced' are read-only kinds that preserve whatever a developer configured."
  );

const slotValueBase = z.object({
  kind: slotValueKind,
  propertyAlias: z.string().nullish().describe("For kind 'property': the Umbraco property alias (or a __ built-in)."),
  editorAlias: z.string().nullish().describe("Optional editor alias of propertyAlias (lets the server add stripHtml for rich text)."),
  fixedValue: z.string().nullish().describe("For kind 'fixed': the literal value (an enum slot's option value, a currency code, a URL…)."),
  targetPieceKey: z.string().nullish().describe("For kind 'siteIdentity': normally 'organization'."),
  confidence: z.number().int().nullish(),
  isAutoMapped: z.boolean().optional(),
  summary: z.string().nullish(),
});

// The SDK's input sanitiser walks object/array/record/union only (no z.lazy), so nesting is
// spelled out: entity slot → parts → one more level of parts (Place.address → PostalAddress).
const slotValueLeaf = slotValueBase.extend({ nested: z.record(z.string(), z.unknown()).optional().describe("[raw]") });
const slotValueInner = slotValueBase.extend({ nested: z.record(z.string(), slotValueLeaf).optional() });
export const slotValueSchema = slotValueBase.extend({
  nested: z.record(z.string(), slotValueInner).optional().describe("For kind 'nested': sub-slot values keyed by the nested type's schemaProperty."),
});
export type SlotValueInput = z.infer<typeof slotValueSchema>;

export const slotSchema = z
  .object({
    schemaProperty: z.string(),
    label: z.string(),
    help: z.string(),
    importance: z.string().describe("required | recommended | optional"),
    kind: z.string().describe("text | image | date | url | number | boolean | duration | enum | list | entity"),
    acceptedEditorAliases: z.array(z.string()),
    builtIns: z.array(z.string()),
    anyOfGroup: z.string().nullish(),
    siteIdentityPieceKey: z.string().nullish(),
    fixedValueOptions: z.array(z.object({ value: z.string(), label: z.string() })),
    nested: z
      .object({ schemaType: z.string(), slots: z.array(z.unknown()) })
      .nullish()
      .describe("For entity slots: the nested Schema.org type and its own slots."),
  })
  .passthrough();

export const templateSchema = z
  .object({
    id: z.string(),
    label: z.string(),
    description: z.string(),
    family: z.string(),
    schemaType: z.string(),
    variants: z.array(z.object({ schemaType: z.string(), label: z.string(), hint: z.string().nullish() })),
    useBusinessTypes: z.boolean(),
    googleFeature: z.object({ name: z.string(), docUrl: z.string(), status: z.string() }),
    aliasHints: z.array(z.string()),
    slots: z.array(slotSchema),
  })
  .passthrough();

export const mappingSummarySchema = z
  .object({
    schemaTypeName: z.string(),
    templateId: z.string().nullish(),
    templateLabel: z.string().nullish(),
    isRetired: z.boolean(),
    isEnabled: z.boolean(),
    requiredFilled: z.number(),
    requiredTotal: z.number(),
    recommendedFilled: z.number(),
    recommendedTotal: z.number(),
    advancedRowCount: z.number(),
    missingRequired: z.array(z.string()),
  })
  .passthrough();

export const contentTypeStatusSchema = z
  .object({
    alias: z.string(),
    name: z.string(),
    key: z.string(),
    icon: z.string().nullish(),
    isElement: z.boolean(),
    hasTemplate: z.boolean(),
    allowedAsRoot: z.boolean(),
    publishedCount: z.number(),
    status: z.string().describe("not-set-up | needs-attention | set-up | developer-managed"),
    mapping: mappingSummarySchema.nullish(),
  })
  .passthrough();

export const projectionSchema = z
  .object({
    templateId: z.string(),
    schemaTypeName: z.string(),
    hasExistingMapping: z.boolean(),
    slots: z.array(z.object({ slot: slotSchema, value: slotValueSchema })),
    advancedRows: z.array(z.unknown()),
    unslottedSuggestions: z.array(z.unknown()),
  })
  .passthrough();

export const siteIdentitySchema = z
  .object({
    kind: z.enum(["organisation", "person"]),
    name: z.string(),
    alternateName: z.string().nullish(),
    url: z.string().nullish(),
    logoMediaKey: z.string().nullish(),
    sameAs: z.array(z.string()).default([]),
    telephone: z.string().nullish(),
    email: z.string().nullish(),
    description: z.string().nullish(),
    businessType: z.string().nullish().describe("A LocalBusiness subtype name (Dentist, Restaurant…) or null for a plain Organization."),
    address: z
      .object({
        streetAddress: z.string().nullish(),
        addressLocality: z.string().nullish(),
        addressRegion: z.string().nullish(),
        postalCode: z.string().nullish(),
        addressCountry: z.string().nullish(),
      })
      .nullish(),
    openingHours: z
      .array(z.object({ dayOfWeek: z.array(z.string()), opens: z.string().nullish(), closes: z.string().nullish() }))
      .default([]),
    geo: z.object({ latitude: z.number(), longitude: z.number() }).nullish(),
    priceRange: z.string().nullish(),
    jobTitle: z.string().nullish(),
    searchUrlTemplate: z.string().nullish(),
    searchQueryInputName: z.string().nullish(),
    updatedDate: z.string().nullish(),
    updatedByUserKey: z.string().nullish(),
    isConfigured: z.boolean().optional(),
  })
  .passthrough();

export const pageOverrideSchema = z
  .object({
    contentKey: z.string(),
    schemaTypeName: z.string().nullish(),
    isExcluded: z.boolean(),
    updatedDate: z.string().nullish(),
  })
  .passthrough();

export const pageSummarySchema = z
  .object({
    contentKey: z.string(),
    contentTypeAlias: z.string(),
    contentTypeName: z.string(),
    hasMapping: z.boolean(),
    templateId: z.string().nullish(),
    templateLabel: z.string().nullish(),
    defaultSchemaTypeName: z.string().nullish(),
    effectiveSchemaTypeName: z.string().nullish(),
    allowedPageTypes: z.array(z.object({ schemaType: z.string(), label: z.string(), hint: z.string().nullish() })),
    override: pageOverrideSchema.nullish(),
    isExcluded: z.boolean(),
    isPublished: z.boolean(),
    publicUrl: z.string().nullish(),
    preview: z.unknown().nullish().describe("The core JsonLdPreviewResponse (jsonLd, isValid, issues…) or null when unpublished/excluded."),
  })
  .passthrough();

export const healthReportSchema = z
  .object({
    generatedAt: z.string(),
    cached: z.boolean(),
    contentTypesChecked: z.number(),
    critical: z.number(),
    warning: z.number(),
    items: z.array(
      z
        .object({
          alias: z.string(),
          name: z.string(),
          schemaTypeName: z.string(),
          templateId: z.string().nullish(),
          samplePageKey: z.string().nullish(),
          samplePageName: z.string().nullish(),
          critical: z.number(),
          warning: z.number(),
          issues: z.array(z.unknown()),
          status: z.string().describe("ok | needs-attention | no-published-page"),
        })
        .passthrough()
    ),
  })
  .passthrough();

export const SEO_NOTE =
  "Requires the Umbraco.Community.SchemeWeaver.Seo package and an API user whose group has the 'SEO Schema' section " +
  "(SchemeWeaver.Seo.Section); otherwise the call returns 403/404.";
