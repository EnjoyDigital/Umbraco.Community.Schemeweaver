import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface ContentTypePickerModalData {
  currentAlias?: string;
}

export interface ContentTypePickerModalValue {
  contentTypeAlias: string;
  contentTypeName: string;
}

export const SCHEMEWEAVER_CONTENT_TYPE_PICKER_MODAL = new UmbModalToken<
  ContentTypePickerModalData,
  ContentTypePickerModalValue
>('SchemeWeaver.Modal.ContentTypePicker', {
  modal: {
    type: 'sidebar',
    size: 'medium',
  },
});
