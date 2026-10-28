import { SEO_SECTION_ALIAS, SEO_SECTION_PATHNAME } from '../constants.js';

// Condition aliases are inlined (as the core does) so this manifest module stays free of
// backoffice imports and the initial bundle stays small.
const SECTION_USER_PERMISSION = 'Umb.Condition.SectionUserPermission';
const SECTION_ALIAS = 'Umb.Condition.SectionAlias';

const inSection = [{ alias: SECTION_ALIAS, match: SEO_SECTION_ALIAS }];

export const manifests: Array<UmbExtensionManifest> = [
  {
    type: 'section',
    alias: SEO_SECTION_ALIAS,
    name: 'SEO Schema Section',
    weight: 250,
    meta: {
      label: '#schemeWeaverSeo_sectionLabel',
      pathname: SEO_SECTION_PATHNAME,
    },
    conditions: [{ alias: SECTION_USER_PERMISSION, match: SEO_SECTION_ALIAS }],
  },
  {
    type: 'sectionView',
    alias: 'SchemeWeaver.Seo.SectionView.Overview',
    name: 'SEO Schema Overview',
    weight: 400,
    element: () => import('./views/overview-view.element.js'),
    meta: { label: '#schemeWeaverSeo_overview', pathname: 'overview', icon: 'icon-dashboard' },
    conditions: inSection,
  },
  {
    type: 'sectionView',
    alias: 'SchemeWeaver.Seo.SectionView.SiteIdentity',
    name: 'SEO Schema Site Identity',
    weight: 300,
    element: () => import('./views/site-identity-view.element.js'),
    meta: { label: '#schemeWeaverSeo_siteIdentity', pathname: 'site-identity', icon: 'icon-company' },
    conditions: inSection,
  },
  {
    type: 'sectionView',
    alias: 'SchemeWeaver.Seo.SectionView.ContentTypes',
    name: 'SEO Schema Content Types',
    weight: 200,
    element: () => import('./views/content-types-view.element.js'),
    meta: { label: '#schemeWeaverSeo_contentTypes', pathname: 'content-types', icon: 'icon-item-arrangement' },
    conditions: inSection,
  },
  {
    type: 'sectionView',
    alias: 'SchemeWeaver.Seo.SectionView.Settings',
    name: 'SEO Schema Settings',
    weight: 100,
    element: () => import('./views/settings-view.element.js'),
    meta: { label: '#schemeWeaverSeo_settings', pathname: 'settings', icon: 'icon-settings' },
    conditions: inSection,
  },
];
