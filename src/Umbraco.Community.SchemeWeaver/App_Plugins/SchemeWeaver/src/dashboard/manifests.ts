import type { ManifestDashboard } from '@umbraco-cms/backoffice/dashboard';

export const manifests: ManifestDashboard[] = [
  {
    type: 'dashboard',
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
