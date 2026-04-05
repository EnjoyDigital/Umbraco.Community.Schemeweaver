export const manifests = [
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Umbraco.Community.SchemeWeaver.AI.Analyse',
    name: 'AI Analyse Schema',
    weight: 102,
    api: () => import('./ai-analyse.action.js'),
    meta: {
      icon: 'icon-wand',
      label: '#schemeWeaver_aiAnalyse',
    },
    forEntityTypes: ['document-type'],
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Umbraco.Community.SchemeWeaver.AI.AnalyseAll',
    name: 'AI Analyse All Schemas',
    weight: 103,
    api: () => import('./ai-analyse-all.action.js'),
    meta: {
      icon: 'icon-wand',
      label: '#schemeWeaver_aiAnalyseAll',
    },
    forEntityTypes: ['document-type-root'],
  },
];
