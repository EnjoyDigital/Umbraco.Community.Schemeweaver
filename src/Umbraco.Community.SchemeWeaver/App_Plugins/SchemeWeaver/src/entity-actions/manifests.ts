// Alias duplicated rather than imported from the condition module: manifests are
// eagerly evaluated at registration, and importing it here would pull the
// condition (and the whole context graph behind it) into the initial bundle.
const HAS_MAPPING_CONDITION = 'SchemeWeaver.Condition.HasMapping';

export const manifests: Array<UmbExtensionManifest> = [
  {
    type: 'condition',
    name: 'SchemeWeaver Has Mapping Condition',
    alias: HAS_MAPPING_CONDITION,
    api: () => import('./has-mapping.condition.js'),
  },
  // Two entries over one dual-purpose item: on an unmapped document type this
  // starts a mapping, on a mapped one it changes the type — different enough
  // jobs that they deserve their own labels, matching the wording of the dialog
  // each one opens.
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'SchemeWeaver.EntityAction.MapToSchema',
    name: 'Map to Schema.org',
    weight: 300,
    api: () => import('./map-to-schema.action.js'),
    meta: {
      icon: 'icon-brackets',
      label: '#schemeWeaver_mapToSchema',
    },
    forEntityTypes: ['document-type'],
    conditions: [{ alias: HAS_MAPPING_CONDITION, match: false }],
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'SchemeWeaver.EntityAction.ChangeSchemaType',
    name: 'Change Schema.org Type',
    weight: 300,
    api: () => import('./map-to-schema.action.js'),
    meta: {
      icon: 'icon-brackets',
      label: '#schemeWeaver_changeSchemaType',
    },
    forEntityTypes: ['document-type'],
    conditions: [{ alias: HAS_MAPPING_CONDITION, match: true }],
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'SchemeWeaver.EntityAction.DeleteSchemaMapping',
    name: 'Delete Schema.org Mapping',
    weight: 200,
    api: () => import('./delete-schema-mapping.action.js'),
    meta: {
      icon: 'icon-trash',
      label: '#schemeWeaver_deleteMapping',
    },
    forEntityTypes: ['document-type'],
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'SchemeWeaver.EntityAction.GenerateFromSchema',
    name: 'Generate from Schema.org',
    weight: 100,
    api: () => import('./generate-from-schema.action.js'),
    meta: {
      icon: 'icon-wand',
      label: '#schemeWeaver_generateFromSchema',
    },
    forEntityTypes: ['document-type'],
  },
];
