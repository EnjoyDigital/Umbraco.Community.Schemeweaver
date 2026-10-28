import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import type { SchemeWeaverSeoContext } from './seo.context.js';

export const SCHEMEWEAVER_SEO_CONTEXT = new UmbContextToken<SchemeWeaverSeoContext>(
  'Umbraco.Community.SchemeWeaver.Seo.Context',
);
