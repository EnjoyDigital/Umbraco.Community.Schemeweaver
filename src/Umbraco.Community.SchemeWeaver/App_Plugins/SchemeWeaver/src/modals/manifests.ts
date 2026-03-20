export const manifests = [
  {
    type: 'modal',
    alias: 'schemeweaver-schema-picker-modal',
    name: 'Schema Picker Modal',
    element: () => import('./schema-picker-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'schemeweaver-property-mapping-modal',
    name: 'Property Mapping Modal',
    element: () => import('./property-mapping-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'schemeweaver-generate-doctype-modal',
    name: 'Generate Document Type Modal',
    element: () => import('./generate-doctype-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'schemeweaver-nested-mapping-modal',
    name: 'Nested Mapping Modal',
    element: () => import('./nested-mapping-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'schemeweaver-content-type-picker-modal',
    name: 'Content Type Picker Modal',
    element: () => import('./content-type-picker-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'SchemeWeaverPropertyPickerModal',
    name: 'Property Picker Modal',
    element: () => import('./property-picker-modal.element.js'),
  },
];
