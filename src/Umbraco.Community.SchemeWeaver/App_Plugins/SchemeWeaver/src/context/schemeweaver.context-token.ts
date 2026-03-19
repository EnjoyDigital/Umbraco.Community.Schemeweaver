import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import type { SchemeWeaverContext } from './schemeweaver.context.js';

export const SCHEMEWEAVER_CONTEXT = new UmbContextToken<SchemeWeaverContext>(
  'SchemeWeaverContext',
);
