import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface ComplexTypeMappingModalData {
  schemaPropertyName: string;
  acceptedTypes: string[];
  selectedSubType: string;
  contentTypeAlias: string;
  availableProperties: string[];
  existingConfig: string | null;
  parentPath?: string;
}

export interface ComplexTypeMappingModalValue {
  resolverConfig: string;
  selectedSubType: string;
}

export const SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL = new UmbModalToken<
  ComplexTypeMappingModalData,
  ComplexTypeMappingModalValue
>('SchemeWeaver.Modal.ComplexTypeMapping', {
  modal: {
    type: 'sidebar',
    size: 'medium',
  },
});
