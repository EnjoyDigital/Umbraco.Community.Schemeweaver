import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface ComplexTypeMappingModalData {
  schemaPropertyName: string;
  acceptedTypes: string[];
  selectedSubType: string;
  contentTypeAlias: string;
  availableProperties: string[];
  existingConfig: string | null;
}

export interface ComplexTypeMappingModalValue {
  resolverConfig: string;
  selectedSubType: string;
}

export const SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL = new UmbModalToken<
  ComplexTypeMappingModalData,
  ComplexTypeMappingModalValue
>('schemeweaver-complex-type-mapping-modal', {
  modal: {
    type: 'sidebar',
    size: 'medium',
  },
});
