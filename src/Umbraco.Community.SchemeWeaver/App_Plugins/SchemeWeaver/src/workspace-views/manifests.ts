export const manifests = [
  {
    type: 'workspaceView',
    alias: 'SchemeWeaver.WorkspaceView.SchemaMapping',
    name: 'Schema.org Mapping',
    element: () => import('./schema-mapping-view.element.js'),
    weight: 90,
    meta: {
      label: 'Schema.org',
      pathname: 'schema-org',
      icon: 'icon-brackets',
    },
    conditions: [
      {
        alias: 'Umb.Condition.WorkspaceAlias',
        match: 'Umb.Workspace.DocumentType',
      },
    ],
  },
];
