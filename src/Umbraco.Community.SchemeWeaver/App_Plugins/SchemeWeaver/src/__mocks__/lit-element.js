import { LitElement } from 'lit';
import { __mockContextRegistry } from './context-api.js';

/** Flat translation map mirroring src/localization/en.ts */
const translations = {
  'schemeWeaver_dashboardHeadline': 'Schema.org Mappings',
  'schemeWeaver_searchContentTypes': 'Search content types...',
  'schemeWeaver_searchSchemaTypes': 'Search schema types...',
  'schemeWeaver_loading': 'Loading...',
  'schemeWeaver_loadingMappings': 'Loading schema mappings...',
  'schemeWeaver_loadingSchemaTypes': 'Loading schema types...',
  'schemeWeaver_loadingProperties': 'Loading property mappings...',
  'schemeWeaver_noResults': 'No content types found matching your search.',
  'schemeWeaver_noSchemaTypes': 'No schema types found.',
  'schemeWeaver_noMapping': 'No Schema.org Mapping',
  'schemeWeaver_noMappingDescription': 'This content type has not been mapped to a Schema.org type yet.',
  'schemeWeaver_notMapped': 'Not mapped',
  'schemeWeaver_mapped': 'Mapped',
  'schemeWeaver_unmapped': 'Unmapped',
  'schemeWeaver_properties': 'Properties',
  'schemeWeaver_save': 'Save Mapping',
  'schemeWeaver_saving': 'Saving...',
  'schemeWeaver_cancel': 'Cancel',
  'schemeWeaver_retry': 'Retry',
  'schemeWeaver_refresh': 'Refresh',
  'schemeWeaver_generate': 'Generate',
  'schemeWeaver_generating': 'Generating...',
  'schemeWeaver_map': 'Map',
  'schemeWeaver_mapToSchema': 'Map to Schema.org',
  'schemeWeaver_editMapping': 'Edit mapping',
  'schemeWeaver_deleteMapping': 'Delete mapping',
  'schemeWeaver_change': 'Change',
  'schemeWeaver_changeSchemaType': 'Change schema type',
  'schemeWeaver_changeSchemaTypeHint': 'Map this document type to a different Schema.org type, keeping the property mappings that still apply',
  'schemeWeaver_changeSchemaTypeIntro': 'Change the Schema.org type from "{0}" to "{1}".',
  'schemeWeaver_changeSchemaTypeKept': '{0} of {1} property mappings will carry over.',
  'schemeWeaver_changeSchemaTypeDropped': 'These property mappings do not exist on "{0}" and will be removed:',
  'schemeWeaver_changeSchemaTypeLoadFailed': 'Could not load the properties of "{0}", so the mapping was left unchanged.',
  'schemeWeaver_schemaTypeChanged': 'Schema type changed to {0}',
  'schemeWeaver_currentType': 'Current',
  'schemeWeaver_previewJsonLd': 'Preview JSON-LD',
  'schemeWeaver_autoMap': 'Auto-map',
  'schemeWeaver_autoMapSchema': 'Auto-map Schema',
  'schemeWeaver_generatePreview': 'Generate Preview',
  'schemeWeaver_selectSchemaType': 'Select Schema.org Type',
  'schemeWeaver_mapProperties': 'Map Properties',
  'schemeWeaver_generateContentType': 'Generate Content Type from Schema.org',
  'schemeWeaver_contentTypeName': 'Content Type Name',
  'schemeWeaver_contentTypeAlias': 'Content Type Alias',
  'schemeWeaver_selectProperties': 'Select Properties',
  'schemeWeaver_selectPropertiesDescription': 'Choose which Schema.org properties to include as document type properties:',
  'schemeWeaver_contentType': 'Content Type',
  'schemeWeaver_schemaType': 'Schema Type',
  'schemeWeaver_status': 'Status',
  'schemeWeaver_actions': 'Actions',
  'schemeWeaver_back': 'Back',
  'schemeWeaver_schemaOrgMapping': 'Schema.org Mapping',
  'schemeWeaver_propertyMappings': 'Property Mappings',
  'schemeWeaver_contentTypeSettings': 'Content Type Settings',
  'schemeWeaver_mappedTo': 'mapped to',
  'schemeWeaver_extends': 'extends',
  'schemeWeaver_generateFromSchema': 'Generate from Schema.org',
  'schemeWeaver_noPreviewData': 'No JSON-LD data to preview',
  'schemeWeaver_jsonLdPreview': 'JSON-LD Preview',
  'schemeWeaver_copyToClipboard': 'Copy to clipboard',
  'schemeWeaver_copy': 'Copy',
  'schemeWeaver_mappingDeleted': 'Mapping deleted successfully',
  'schemeWeaver_mappingSaved': 'Mapping saved successfully',
  'schemeWeaver_preview': 'Preview',
  'schemeWeaver_loadMappingsFailed': 'Failed to load mappings',
  'schemeWeaver_valid': 'Valid',
  'schemeWeaver_invalid': 'Invalid',
  'general_submit': 'Submit',
  // Row-scoped block-mapping panel
  'schemeWeaver_blockMappingsFor': 'Map blocks to {0}',
  'schemeWeaver_blockModalContext': 'Blocks mapped here are output as the {0} property of {1}.',
  'schemeWeaver_blockModalEditorKind': 'Block List/Grid',
  'schemeWeaver_blockModalAccepts': 'Accepts: {0}',
  'schemeWeaver_mappedViaProperty': 'Mapped via {0}',
  'schemeWeaver_suggestedTypeViaProperty': 'Suggested: {0} via {1}',
  'schemeWeaver_otherSchemaType': 'Other type…',
  'schemeWeaver_blockMappedCount': '{0} mapped',
  'schemeWeaver_blockMappedCountDetail': '{0} of {1} properties mapped · {2} of {3} recommended',
  'schemeWeaver_blockAutoMapSkipped': '{0} block(s) fit other properties and were not mapped here: {1}',
  'schemeWeaver_blockFanOutOffer': '{0} block(s) fit other properties: {1}',
  'schemeWeaver_blockFanOutCreate': 'Create rows for other properties',
  'schemeWeaver_blockFanOutCreated': 'Added mapping rows for: {0}',
  'schemeWeaver_stringListNotice': 'This mapping extracts a text list from {0} — there are no block routes to edit. Saving keeps the string-list configuration unchanged.',
  'schemeWeaver_mapThisBlock': 'Map this block',
};

/**
 * Resolve a localisation key to its English value, falling back to the key
 * itself, interpolating `{0}`/`{1}`… tokens like the real UmbLocalizationController.
 */
export function resolveLocalizationKey(key, ...args) {
  let text = translations[key] || key;
  args.forEach((arg, i) => {
    text = text.replaceAll(`{${i}}`, String(arg));
  });
  return text;
}

const localize = {
  term: (key, ...args) => resolveLocalizationKey(key, ...args),
};

export class UmbLitElement extends LitElement {
  constructor() {
    super();
    this.localize = localize;
  }

  observe(observable, callback, alias) {
    if (observable && typeof observable.getValue === 'function') {
      callback(observable.getValue());
    }
  }

  async getContext(token) {
    return __mockContextRegistry.consume(token);
  }

  consumeContext(token, callback) {
    const instance = __mockContextRegistry.consume(token);
    if (instance) callback(instance);
    return { destroy() {} };
  }

  provideContext(token, instance) {
    __mockContextRegistry.provide(token, instance);
  }
}

/** Minimal <umb-localize> stub that renders translation text in light DOM. */
if (!customElements.get('umb-localize')) {
  class UmbLocalizeElement extends HTMLElement {
    static get observedAttributes() {
      return ['key'];
    }
    connectedCallback() {
      this._render();
    }
    attributeChangedCallback() {
      this._render();
    }
    _render() {
      const key = this.getAttribute('key');
      this.textContent = key ? resolveLocalizationKey(key) : '';
    }
  }
  customElements.define('umb-localize', UmbLocalizeElement);
}

/** Minimal <umb-body-layout> stub that renders slot content. */
if (!customElements.get('umb-body-layout')) {
  class UmbBodyLayout extends HTMLElement {
    constructor() {
      super();
      this.attachShadow({ mode: 'open' });
      this.shadowRoot.innerHTML = '<slot></slot><slot name="actions"></slot>';
    }
  }
  customElements.define('umb-body-layout', UmbBodyLayout);
}
