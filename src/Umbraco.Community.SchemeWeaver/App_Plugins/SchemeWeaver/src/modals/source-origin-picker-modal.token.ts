import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface SourceOriginPickerModalData {
  editorAlias: string;
  isComplexType: boolean;
  currentSourceType: string;
  restrictToSimpleSources?: boolean;
  /**
   * Hide only the Block Content option. Used by the complex-type modal: nested
   * sub-rows may source from related nodes, but block content has no meaning
   * inside a nested config.
   */
  hideBlockContent?: boolean;
}

export interface SourceOriginPickerModalValue {
  sourceType: string;
}

export const SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL = new UmbModalToken<
  SourceOriginPickerModalData,
  SourceOriginPickerModalValue
>('SchemeWeaver.Modal.SourceOriginPicker', {
  modal: {
    type: 'sidebar',
    size: 'small',
  },
});
