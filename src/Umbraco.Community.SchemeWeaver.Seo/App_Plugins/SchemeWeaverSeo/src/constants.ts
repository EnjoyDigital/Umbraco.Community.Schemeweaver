/**
 * Shared aliases and routes for the SchemeWeaver SEO bundle.
 *
 * SEO_SECTION_ALIAS is a wire contract with the backend: it is the alias of the `section`
 * manifest, the string stored on user groups' allowed sections, and the value the API's
 * section-access policy compares against. `SchemeWeaverSeoConstants.SectionAlias` in C# must
 * match it exactly (an integration test asserts this file contains it).
 */
export const SEO_SECTION_ALIAS = 'SchemeWeaver.Seo.Section';
export const SEO_SECTION_PATHNAME = 'seo-schema';

export const SEO_API_BASE = '/umbraco/management/api/v1/schemeweaver-seo';
export const CORE_API_BASE = '/umbraco/management/api/v1/schemeweaver';

export const SEO_SETUP_WIZARD_MODAL_ALIAS = 'SchemeWeaver.Seo.Modal.SetupWizard';
export const SEO_PAGE_SCHEMA_VIEW_ALIAS = 'SchemeWeaver.Seo.WorkspaceView.PageSchema';
export const CORE_JSONLD_VIEW_ALIAS = 'SchemeWeaver.WorkspaceView.JsonLdPreview';

export const GOOGLE_RICH_RESULTS_TEST_URL = 'https://search.google.com/test/rich-results';

/** Same thresholds as the core mapping table. */
export const CONFIDENCE_HIGH = 80;
export const CONFIDENCE_MEDIUM = 50;
