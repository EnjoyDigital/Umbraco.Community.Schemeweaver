export const manifests = [
  {
    type: 'dashboard' as const,
    alias: 'SchemeWeaver.Dashboard',
    name: 'SchemeWeaver Schema Mappings',
    element: () => import('./schema-mappings-dashboard.element.js'),
    weight: 10,
    meta: {
      label: '#schemeWeaver_dashboardHeadline',
      pathname: 'schema-mappings',
    },
    conditions: [
      {
        alias: 'Umb.Condition.SectionAlias',
        match: 'Umb.Section.Settings',
      },
    ],
  },
];
