import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface GenerateDoctypeModalData {
  contentTypeAlias: string;
}

export interface GenerateDoctypeModalValue {
  generated: boolean;
}

export const SCHEMEWEAVER_GENERATE_DOCTYPE_MODAL = new UmbModalToken<
  GenerateDoctypeModalData,
  GenerateDoctypeModalValue
>('SchemeWeaver.Modal.GenerateDoctype', {
  modal: {
    type: 'sidebar',
    size: 'large',
  },
});
