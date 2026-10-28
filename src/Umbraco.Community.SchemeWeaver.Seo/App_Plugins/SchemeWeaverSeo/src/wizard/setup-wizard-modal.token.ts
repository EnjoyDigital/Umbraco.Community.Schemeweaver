import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import { SEO_SETUP_WIZARD_MODAL_ALIAS } from '../constants.js';

export interface SeoSetupWizardModalData {
  contentTypeAlias: string;
  contentTypeName: string;
  contentTypeKey: string;
}

export interface SeoSetupWizardModalValue {
  saved: boolean;
  schemaTypeName?: string;
}

export const SEO_SETUP_WIZARD_MODAL = new UmbModalToken<SeoSetupWizardModalData, SeoSetupWizardModalValue>(
  SEO_SETUP_WIZARD_MODAL_ALIAS,
  { modal: { type: 'sidebar', size: 'large' } },
);
