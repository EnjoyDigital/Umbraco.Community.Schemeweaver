import { UMB_WORKSPACE_CONDITION_ALIAS } from '@umbraco-cms/backoffice/workspace';
import { SEO_PAGE_SCHEMA_VIEW_ALIAS, SEO_SECTION_ALIAS } from '../constants.js';

export const manifests: Array<UmbExtensionManifest> = [
  {
    type: 'workspaceView',
    alias: SEO_PAGE_SCHEMA_VIEW_ALIAS,
    name: 'Page Schema',
    element: () => import('./page-schema-view.element.js'),
    weight: 80,
    meta: { label: '#schemeWeaverSeo_pageSchemaTab', pathname: 'schema', icon: 'icon-globe' },
    conditions: [
      { alias: UMB_WORKSPACE_CONDITION_ALIAS, match: 'Umb.Workspace.Document' },
      // Only users who may use the SEO section get the tab (its API requires that permission).
      { alias: 'Umb.Condition.SectionUserPermission', match: SEO_SECTION_ALIAS },
    ],
  },
];
