import type { ManifestWorkspaceView } from '@umbraco-cms/backoffice/workspace';
import { UMB_WORKSPACE_CONDITION_ALIAS } from '@umbraco-cms/backoffice/workspace';

export const manifests: ManifestWorkspaceView[] = [
  {
    type: 'workspaceView',
    alias: 'SchemeWeaver.WorkspaceView.SchemaMapping',
    name: 'Schema.org Mapping',
    element: () => import('./schema-mapping-view.element.js'),
    weight: 90,
    meta: {
      label: '#schemeWeaver_workspaceViewSchemaOrg',
      pathname: 'schema-org',
      icon: 'icon-brackets',
    },
    conditions: [
      {
        alias: UMB_WORKSPACE_CONDITION_ALIAS,
        match: 'Umb.Workspace.DocumentType',
      },
    ],
  },
  {
    type: 'workspaceView',
    alias: 'SchemeWeaver.WorkspaceView.JsonLdPreview',
    name: 'JSON-LD Preview',
    element: () => import('./jsonld-content-view.element.js'),
    weight: 80,
    meta: {
      label: '#schemeWeaver_workspaceViewJsonLd',
      pathname: 'json-ld',
      icon: 'icon-brackets',
    },
    conditions: [
      {
        alias: UMB_WORKSPACE_CONDITION_ALIAS,
        match: 'Umb.Workspace.Document',
      },
    ],
  },
];
