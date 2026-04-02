import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface SourceOriginPickerModalData {
  editorAlias: string;
  isComplexType: boolean;
  currentSourceType: string;
  restrictToSimpleSources?: boolean;
}

export interface SourceOriginPickerModalValue {
  sourceType: string;
}

export const SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL = new UmbModalToken<
  SourceOriginPickerModalData,
  SourceOriginPickerModalValue
>('schemeweaver-source-origin-picker-modal', {
  modal: {
    type: 'sidebar',
    size: 'small',
  },
});
