import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface NestedMappingModalData {
  nestedSchemaTypeName: string;
  contentTypePropertyAlias: string;
  contentTypeAlias: string;
  existingConfig: string | null;
}

export interface NestedMappingModalValue {
  resolverConfig: string;
}

export const SCHEMEWEAVER_NESTED_MAPPING_MODAL = new UmbModalToken<
  NestedMappingModalData,
  NestedMappingModalValue
>('SchemeWeaver.Modal.NestedMapping', {
  modal: {
    type: 'sidebar',
    size: 'medium',
  },
});
