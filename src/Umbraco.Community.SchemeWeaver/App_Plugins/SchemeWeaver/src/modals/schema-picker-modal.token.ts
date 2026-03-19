import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface SchemaPickerModalData {
  contentTypeAlias: string;
}

export interface SchemaPickerModalValue {
  schemaType: string;
}

export const SCHEMEWEAVER_SCHEMA_PICKER_MODAL = new UmbModalToken<
  SchemaPickerModalData,
  SchemaPickerModalValue
>('schemeweaver-schema-picker-modal', {
  modal: {
    type: 'sidebar',
    size: 'medium',
  },
});
