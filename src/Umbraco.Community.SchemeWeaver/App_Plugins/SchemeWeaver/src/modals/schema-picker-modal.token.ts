import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface SchemaPickerModalData {
  contentTypeAlias: string;
  /**
   * The type this content type is already mapped to, when re-picking. Pre-selects
   * it and tags it in the list so the user can see where they are starting from.
   */
  currentSchemaType?: string;
}

export interface SchemaPickerModalValue {
  schemaType: string;
}

export const SCHEMEWEAVER_SCHEMA_PICKER_MODAL = new UmbModalToken<
  SchemaPickerModalData,
  SchemaPickerModalValue
>('SchemeWeaver.Modal.SchemaPicker', {
  modal: {
    type: 'sidebar',
    size: 'medium',
  },
});
