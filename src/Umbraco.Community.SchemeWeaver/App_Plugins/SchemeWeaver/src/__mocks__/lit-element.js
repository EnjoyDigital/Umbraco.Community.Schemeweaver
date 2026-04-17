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
};

/** Resolve a localisation key to its English value, falling back to the key itself. */
export function resolveLocalizationKey(key) {
  return translations[key] || key;
}

const localize = {
  term: (key) => resolveLocalizationKey(key),
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
