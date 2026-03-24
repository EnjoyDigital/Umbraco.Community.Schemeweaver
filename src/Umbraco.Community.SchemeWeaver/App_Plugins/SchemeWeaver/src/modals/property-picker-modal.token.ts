import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface PropertyPickerModalData {
  contentTypeAlias: string;
}

export interface PropertyPickerModalValue {
  propertyAlias: string;
}

export const SCHEMEWEAVER_PROPERTY_PICKER_MODAL = new UmbModalToken<
  PropertyPickerModalData,
  PropertyPickerModalValue
>('schemeweaver-property-picker-modal', {
  modal: {
    type: 'sidebar',
    size: 'medium',
  },
});
