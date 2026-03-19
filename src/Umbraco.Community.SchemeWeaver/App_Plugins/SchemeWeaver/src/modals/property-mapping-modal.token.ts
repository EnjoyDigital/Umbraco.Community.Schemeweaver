import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface PropertyMappingModalData {
  contentTypeAlias: string;
  schemaType: string;
}

export interface PropertyMappingModalValue {
  saved: boolean;
}

export const SCHEMEWEAVER_PROPERTY_MAPPING_MODAL = new UmbModalToken<
  PropertyMappingModalData,
  PropertyMappingModalValue
>('schemeweaver-property-mapping-modal', {
  modal: {
    type: 'sidebar',
    size: 'large',
  },
});
