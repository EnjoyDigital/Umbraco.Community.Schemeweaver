import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface PropertyMappingModalData {
  contentTypeAlias: string;
  schemaType: string;
  contentTypeKey?: string;
}

export interface PropertyMappingModalValue {
  saved: boolean;
}

export const SCHEMEWEAVER_PROPERTY_MAPPING_MODAL = new UmbModalToken<
  PropertyMappingModalData,
  PropertyMappingModalValue
>('SchemeWeaver.Modal.PropertyMapping', {
  modal: {
    type: 'sidebar',
    size: 'large',
  },
});
