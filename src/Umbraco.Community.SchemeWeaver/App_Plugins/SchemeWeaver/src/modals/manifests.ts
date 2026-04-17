import type { ManifestModal } from '@umbraco-cms/backoffice/modal';

export const manifests: ManifestModal[] = [
  {
    type: 'modal',
    alias: 'SchemeWeaver.Modal.SchemaPicker',
    name: 'Schema Picker Modal',
    element: () => import('./schema-picker-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'SchemeWeaver.Modal.PropertyMapping',
    name: 'Property Mapping Modal',
    element: () => import('./property-mapping-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'SchemeWeaver.Modal.GenerateDoctype',
    name: 'Generate Document Type Modal',
    element: () => import('./generate-doctype-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'SchemeWeaver.Modal.NestedMapping',
    name: 'Nested Mapping Modal',
    element: () => import('./nested-mapping-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'SchemeWeaver.Modal.PropertyPicker',
    name: 'Property Picker Modal',
    element: () => import('./property-picker-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'SchemeWeaver.Modal.SourceOriginPicker',
    name: 'Source Origin Picker Modal',
    element: () => import('./source-origin-picker-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'SchemeWeaver.Modal.ComplexTypeMapping',
    name: 'Complex Type Mapping Modal',
    element: () => import('./complex-type-mapping-modal.element.js'),
  },
  {
    type: 'modal',
    alias: 'SchemeWeaver.Modal.ContentTypePicker',
    name: 'Content Type Picker Modal',
    element: () => import('./content-type-picker-modal.element.js'),
  },
];
