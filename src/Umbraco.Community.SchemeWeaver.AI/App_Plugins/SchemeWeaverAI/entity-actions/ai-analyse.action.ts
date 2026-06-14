import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';

const API_BASE = '/umbraco/management/api/v1/schemeweaver';

export class AIAnalyseAction extends UmbEntityActionBase<never> {
  async execute() {
    const localize = new UmbLocalizationController(this);
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    const notificationContext = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    if (!modalManager) return;

    // Resolve GUID to alias
    let contentTypeAlias: string | undefined;
    try {
      const response = await fetch(`${API_BASE}/content-types`);
      if (response.ok) {
        const contentTypes = await response.json() as { alias: string; key: string }[];
        contentTypeAlias = contentTypes.find((ct) => ct.key === this.args.unique)?.alias;
      }
    } catch {
      // fall through
    }

    if (!contentTypeAlias) {
      notificationContext?.peek('danger', {
        data: { message: localize.term('schemeWeaver_failedToResolveContentType') },
      });
      return;
    }

    // Call AI to suggest schema type
    notificationContext?.peek('default', {
      data: { message: localize.term('schemeWeaver_aiAnalysing') },
    });

    try {
      const response = await fetch(
        `${API_BASE}/ai/suggest-schema-type/${encodeURIComponent(contentTypeAlias)}`,
        { method: 'POST' },
      );

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const suggestions = await response.json() as { schemaTypeName: string; confidence: number; reasoning: string | null }[];

      if (!suggestions || suggestions.length === 0) {
        notificationContext?.peek('warning', {
          data: { message: localize.term('schemeWeaver_aiNoSuggestions') },
        });
        return;
      }

      // Use the top suggestion — open schema picker pre-selected, then property mapping
      const topSuggestion = suggestions[0];

      notificationContext?.peek('positive', {
        data: { message: `AI suggests: ${topSuggestion.schemaTypeName} (${topSuggestion.confidence}% confidence)` },
      });

      // Import schema picker and property mapping modal tokens dynamically
      const { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } = await import(
        /* @vite-ignore */
        '/App_Plugins/SchemeWeaver/dist/modals/schema-picker-modal.token.js'
      );
      const { SCHEMEWEAVER_PROPERTY_MAPPING_MODAL } = await import(
        /* @vite-ignore */
        '/App_Plugins/SchemeWeaver/dist/modals/property-mapping-modal.token.js'
      );

      // Open schema picker (user can change the AI suggestion)
      const pickerResult = await modalManager
        .open(this, SCHEMEWEAVER_SCHEMA_PICKER_MODAL, {
          data: { contentTypeAlias },
        })
        .onSubmit()
        .catch(() => null);

      if (!pickerResult?.schemaType) return;

      // Open property mapping modal
      const mappingResult = await modalManager
        .open(this, SCHEMEWEAVER_PROPERTY_MAPPING_MODAL, {
          data: {
            contentTypeAlias,
            schemaType: pickerResult.schemaType,
            contentTypeKey: this.args.unique ?? '',
          },
        })
        .onSubmit()
        .catch(() => null);

      if (mappingResult !== null) {
        notificationContext?.peek('positive', {
          data: { message: localize.term('schemeWeaver_mappingSaved') },
        });
      }
    } catch (error) {
      console.error('SchemeWeaver AI: Analysis failed:', error);
      notificationContext?.peek('danger', {
        data: { message: 'AI analysis failed. Please try again.' },
      });
    }
  }
}

export { AIAnalyseAction as api };
