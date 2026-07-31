import { UmbConditionBase } from '@umbraco-cms/backoffice/extension-registry';
import type { UmbConditionConfigBase } from '@umbraco-cms/backoffice/extension-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UMB_ENTITY_CONTEXT } from '@umbraco-cms/backoffice/entity';
import { SCHEMEWEAVER_CONTEXT } from '../context/schemeweaver.context-token.js';

export const SCHEMEWEAVER_HAS_MAPPING_CONDITION_ALIAS = 'SchemeWeaver.Condition.HasMapping';

export interface SchemeWeaverHasMappingConditionConfig extends UmbConditionConfigBase {
  /** `true` permits only mapped document types, `false` only unmapped ones. */
  match: boolean;
}

/**
 * Permits an extension depending on whether the document type in context already
 * has a SchemeWeaver mapping — used to show "Map to Schema.org" on unmapped
 * document types and "Change Schema.org type" on mapped ones, rather than one
 * entry that silently does two different jobs.
 *
 * Evaluated per menu open rather than cached: creating or deleting a mapping
 * must flip which entry appears, and a cached answer would show the wrong one.
 *
 * Fails OPEN for `match: false` (i.e. shows "Map to Schema.org") if the mapping
 * cannot be determined. Losing the menu entry entirely would strand the user
 * with no way in, and that path is no longer destructive on an existing mapping.
 */
export class SchemeWeaverHasMappingCondition extends UmbConditionBase<SchemeWeaverHasMappingConditionConfig> {
  constructor(host: UmbControllerHost, args: { config: SchemeWeaverHasMappingConditionConfig; onChange: () => void }) {
    super(host, args);

    this.consumeContext(UMB_ENTITY_CONTEXT, (entityContext) => {
      this.observe(
        entityContext?.unique,
        (unique) => {
          this.#evaluate(unique ?? undefined);
        },
        '_observeSchemeWeaverConditionUnique',
      );
    });
  }

  async #evaluate(unique: string | undefined) {
    const wantsMapping = this.config.match !== false;

    if (!unique) {
      this.permitted = !wantsMapping;
      return;
    }

    try {
      const context = await this.getContext(SCHEMEWEAVER_CONTEXT);
      if (!context) throw new Error('SchemeWeaverContext not provided');

      const alias = await context.resolveContentTypeAlias(unique);
      if (!alias) throw new Error(`No content type for ${unique}`);

      // `undefined` is a definite "no mapping" (an expected 404); anything else
      // throws and lands in the catch, where we fail open.
      const mapping = await context.requestMapping(alias);
      this.permitted = !!mapping === wantsMapping;
    } catch {
      this.permitted = !wantsMapping;
    }
  }
}

export { SchemeWeaverHasMappingCondition as api };
