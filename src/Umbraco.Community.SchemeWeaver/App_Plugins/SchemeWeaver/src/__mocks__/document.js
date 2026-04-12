// Stub for @umbraco-cms/backoffice/document
// Prevents the real package from loading and double-registering
// the umb-localize custom element in test mode.
export const UMB_DOCUMENT_WORKSPACE_CONTEXT = Symbol.for('UMB_DOCUMENT_WORKSPACE_CONTEXT');
