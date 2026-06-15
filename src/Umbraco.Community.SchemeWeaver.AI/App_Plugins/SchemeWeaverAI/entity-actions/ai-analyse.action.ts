import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT, UmbModalToken } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbContextToken, UmbContextMinimal } from '@umbraco-cms/backoffice/context-api';

const API_BASE = '/umbraco/management/api/v1/schemeweaver';

type ContextHost = UmbControllerHost & {
  getContext<TContext extends UmbContextMinimal>(token: UmbContextToken<TContext>): Promise<TContext>;
};

async function getAuthHeaders(host: UmbControllerHost): Promise<Record<string, string>> {
  try {
    const authContext = await (host as ContextHost).getContext(UMB_AUTH_CONTEXT);
    const config = authContext.getOpenApiConfiguration();
    const token = typeof config.token === 'function' ? await config.token() : undefined;
    return token ? { Authorization: `Bearer ${token}` } : {};
  } catch {
    return {};
  }
}

async function fetchApi<T>(
  host: UmbControllerHost,
  path: string,
  options: RequestInit = {},
): Promise<T | undefined> {
  const { data } = await tryExecute(
    host,
    (async () => {
      const authHeaders = await getAuthHeaders(host);
      const response = await fetch(`${API_BASE}${path}`, {
        ...options,
        headers: {
          ...authHeaders,
          ...options.headers,
        },
      });

      if (!response.ok) {
        const errorText = await response.text().catch(() => 'Unknown error');
        throw new Error(errorText || `HTTP ${response.status}`);
      }

      if (response.status === 204) {
        return { data: undefined as T };
      }

      const json = await response.json();
      return { data: json as T };
    })(),
  );

  return data;
}

/**
 * B-2 fix: construct local UmbModalToken instances with the IDENTICAL alias
 * strings used by the main SchemeWeaver package. A modal token resolves by its
 * alias, so these open the already-registered main-package modals without any
 * dynamic import of dist paths (which don't exist at runtime).
 */
const SCHEMEWEAVER_SCHEMA_PICKER_MODAL = new UmbModalToken<
  { contentTypeAlias: string },
  { schemaType: string }
>('SchemeWeaver.Modal.SchemaPicker', {
  modal: { type: 'sidebar', size: 'medium' },
});

const SCHEMEWEAVER_PROPERTY_MAPPING_MODAL = new UmbModalToken<
  { contentTypeAlias: string; schemaType: string; contentTypeKey?: string },
  { saved: boolean }
>('SchemeWeaver.Modal.PropertyMapping', {
  modal: { type: 'sidebar', size: 'large' },
});

export class AIAnalyseAction extends UmbEntityActionBase<never> {
  // M-3 fix: controller created once as a class field, not leaked per execute() call
  #localize = new UmbLocalizationController(this);

  async execute() {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    const notificationContext = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    if (!modalManager) return;

    // M-1 fix: use authenticated fetchApi to resolve GUID → alias
    const contentTypes = await fetchApi<{ alias: string; key: string }[]>(this, '/content-types');
    const contentTypeAlias = contentTypes?.find((ct) => ct.key === this.args.unique)?.alias;

    if (!contentTypeAlias) {
      notificationContext?.peek('danger', {
        data: { message: this.#localize.term('schemeWeaver_failedToResolveContentType') },
      });
      return;
    }

    // M-1 + N-7 fix: call AI endpoint with auth, no premature 'Analysing' toast
    try {
      const suggestions = await fetchApi<{ schemaTypeName: string; confidence: number; reasoning: string | null }[]>(
        this,
        `/ai/suggest-schema-type/${encodeURIComponent(contentTypeAlias)}`,
        { method: 'POST' },
      );

      if (!suggestions || suggestions.length === 0) {
        notificationContext?.peek('warning', {
          data: { message: this.#localize.term('schemeWeaver_aiNoSuggestions') },
        });
        return;
      }

      // B-2 fix: open main-package modals by matching alias strings — no dist-path dynamic import
      const pickerResult = await modalManager
        .open(this, SCHEMEWEAVER_SCHEMA_PICKER_MODAL, {
          data: { contentTypeAlias },
        })
        .onSubmit()
        .catch(() => null);

      if (!pickerResult?.schemaType) return;

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

      // N-6 fix: check saved flag, not null
      if (mappingResult?.saved === true) {
        notificationContext?.peek('positive', {
          data: { message: this.#localize.term('schemeWeaver_mappingSaved') },
        });
      }
    } catch (error) {
      console.error('SchemeWeaver AI: Analysis failed:', error);
      notificationContext?.peek('danger', {
        data: { message: this.#localize.term('schemeWeaver_aiAnalysisFailed') },
      });
    }
  }
}

export { AIAnalyseAction as api };
