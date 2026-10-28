import { SEO_SETUP_WIZARD_MODAL_ALIAS } from '../constants.js';

export const manifests: Array<UmbExtensionManifest> = [
  {
    type: 'modal',
    alias: SEO_SETUP_WIZARD_MODAL_ALIAS,
    name: 'SEO Schema Set-up Wizard',
    element: () => import('./setup-wizard-modal.element.js'),
  },
];
