import type { JsonLdPreviewResponse, PropertyMappingDto, PropertyMappingSuggestion, ValidationIssue } from '../core-bridge/types.js';

// Mirrors Models/Api/SeoApiModels.cs and Templates/SeoTemplate.cs (camelCase on the wire).

export type SlotImportance = 'required' | 'recommended' | 'optional';
export type SlotKind = 'text' | 'image' | 'date' | 'url' | 'number' | 'boolean' | 'duration' | 'enum' | 'list' | 'entity';
export type SlotValueKind = 'none' | 'property' | 'fixed' | 'siteIdentity' | 'nested' | 'blocks' | 'advanced';
export type FeatureStatus = 'rich-result' | 'foundation' | 'retired';
export type ContentTypeStatusKind = 'not-set-up' | 'needs-attention' | 'set-up' | 'developer-managed';

export interface SeoTemplateVariant {
  schemaType: string;
  label: string;
  hint?: string;
}

export interface SeoGoogleFeature {
  name: string;
  docUrl: string;
  status: FeatureStatus;
}

export interface SeoDefaultSource {
  sourceType: string;
  alias?: string | null;
  targetPieceKey?: string | null;
  staticValue?: string | null;
}

export interface SeoFixedValueOption {
  value: string;
  label: string;
}

export interface SeoNestedTemplate {
  schemaType: string;
  slots: SeoSlot[];
}

export interface SeoSlot {
  schemaProperty: string;
  label: string;
  help: string;
  importance: SlotImportance;
  kind: SlotKind;
  acceptedEditorAliases: string[];
  builtIns: string[];
  anyOfGroup?: string | null;
  siteIdentityPieceKey?: string | null;
  defaultSource?: SeoDefaultSource | null;
  fixedValueOptions: SeoFixedValueOption[];
  nested?: SeoNestedTemplate | null;
}

export interface SeoTemplate {
  id: string;
  label: string;
  description: string;
  family: string;
  icon: string;
  schemaType: string;
  variants: SeoTemplateVariant[];
  useBusinessTypes: boolean;
  googleFeature: SeoGoogleFeature;
  aliasHints: string[];
  slots: SeoSlot[];
}

export interface SeoRetiredFeature {
  schemaType: string;
  name: string;
  retiredOn: string;
  docUrl: string;
}

export interface SeoBusinessType {
  schemaType: string;
  label: string;
}

export interface SeoTemplateCatalogue {
  templates: SeoTemplate[];
  retired: SeoRetiredFeature[];
  businessTypes: SeoBusinessType[];
}

export interface SeoServerContext {
  sectionAlias: string;
  siteUrl: string | null;
  siteIdentityConfigured: boolean;
  hideDeveloperJsonLdTab: boolean;
  mappingsManagedAsCode: boolean;
  uSyncBootImport: string;
  exportMappingsToUSyncOnSave: boolean;
  useGraphModel: boolean;
  emitBreadcrumbsInDeliveryApi: boolean;
  publicSiteUrl: string | null;
  hasPublishedContent: boolean;
}

export interface SeoMappingSummary {
  schemaTypeName: string;
  templateId: string | null;
  templateLabel: string | null;
  isRetired: boolean;
  isEnabled: boolean;
  requiredFilled: number;
  requiredTotal: number;
  recommendedFilled: number;
  recommendedTotal: number;
  advancedRowCount: number;
  missingRequired: string[];
}

export interface SeoContentTypeStatus {
  alias: string;
  name: string;
  key: string;
  icon: string | null;
  isElement: boolean;
  hasTemplate: boolean;
  allowedAsRoot: boolean;
  publishedCount: number;
  status: ContentTypeStatusKind;
  mapping: SeoMappingSummary | null;
}

export interface SeoSlotValue {
  kind: SlotValueKind;
  propertyAlias?: string | null;
  editorAlias?: string | null;
  fixedValue?: string | null;
  targetPieceKey?: string | null;
  confidence?: number | null;
  isAutoMapped?: boolean;
  nested?: Record<string, SeoSlotValue>;
  summary?: string | null;
}

export interface SeoSlotState {
  slot: SeoSlot;
  value: SeoSlotValue;
}

export interface SeoProjection {
  templateId: string;
  schemaTypeName: string;
  hasExistingMapping: boolean;
  slots: SeoSlotState[];
  advancedRows: PropertyMappingDto[];
  unslottedSuggestions: PropertyMappingSuggestion[];
}

export interface SeoSetupRequest {
  templateId: string;
  schemaTypeName?: string | null;
}

export interface SeoMappingSaveRequest {
  templateId: string;
  schemaTypeName?: string | null;
  slots: Record<string, SeoSlotValue>;
}

export interface SeoSamplePage {
  key: string;
  name: string;
  url: string | null;
  updateDate: string;
}

export interface SiteIdentityAddress {
  streetAddress?: string | null;
  addressLocality?: string | null;
  addressRegion?: string | null;
  postalCode?: string | null;
  addressCountry?: string | null;
}

export interface SiteIdentityOpeningHours {
  dayOfWeek: string[];
  opens?: string | null;
  closes?: string | null;
}

export interface SiteIdentity {
  kind: 'organisation' | 'person';
  name: string;
  alternateName?: string | null;
  url?: string | null;
  logoMediaKey?: string | null;
  sameAs: string[];
  telephone?: string | null;
  email?: string | null;
  description?: string | null;
  businessType?: string | null;
  address?: SiteIdentityAddress | null;
  openingHours: SiteIdentityOpeningHours[];
  geo?: { latitude: number; longitude: number } | null;
  priceRange?: string | null;
  jobTitle?: string | null;
  searchUrlTemplate?: string | null;
  searchQueryInputName?: string | null;
  updatedDate?: string | null;
  isConfigured: boolean;
}

export interface SeoPageOverride {
  contentKey: string;
  schemaTypeName: string | null;
  isExcluded: boolean;
  updatedDate: string | null;
}

export interface SeoPageOverrideRequest {
  schemaTypeName?: string | null;
  isExcluded: boolean;
}

export interface SeoPageSummary {
  contentKey: string;
  contentTypeAlias: string;
  contentTypeName: string;
  hasMapping: boolean;
  templateId: string | null;
  templateLabel: string | null;
  defaultSchemaTypeName: string | null;
  effectiveSchemaTypeName: string | null;
  allowedPageTypes: SeoTemplateVariant[];
  override: SeoPageOverride | null;
  isExcluded: boolean;
  isPublished: boolean;
  publicUrl: string | null;
  preview: JsonLdPreviewResponse | null;
}

export interface SeoHealthItem {
  alias: string;
  name: string;
  schemaTypeName: string;
  templateId: string | null;
  samplePageKey: string | null;
  samplePageName: string | null;
  critical: number;
  warning: number;
  issues: ValidationIssue[];
  status: 'ok' | 'needs-attention' | 'no-published-page';
}

export interface SeoHealthReport {
  generatedAt: string;
  cached: boolean;
  contentTypesChecked: number;
  critical: number;
  warning: number;
  items: SeoHealthItem[];
}

export const EMPTY_SITE_IDENTITY: SiteIdentity = {
  kind: 'organisation',
  name: '',
  sameAs: [],
  openingHours: [],
  isConfigured: false,
};
