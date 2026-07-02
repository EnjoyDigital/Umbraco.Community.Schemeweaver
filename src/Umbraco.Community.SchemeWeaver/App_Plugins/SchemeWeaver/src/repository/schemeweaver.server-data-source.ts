import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbContextToken, UmbContextMinimal } from '@umbraco-cms/backoffice/context-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';

/**
 * Narrow `UmbControllerHost` to expose `getContext`. The base type ships
 * `getContext` on `UmbControllerBase` (which all hosts extend) but the
 * interface itself does not surface it — this intersection avoids a `as any`.
 */
type ContextHost = UmbControllerHost & {
  getContext<TContext extends UmbContextMinimal>(token: UmbContextToken<TContext>): Promise<TContext>;
};
import type {
  SchemaTypeInfo,
  SchemaPropertyInfo,
  RankedSchemaPropertyInfo,
  ContentTypeInfo,
  ContentTypeProperty,
  SchemaMappingDto,
  PropertyMappingSuggestion,
  JsonLdPreviewResponse,
  ContentTypeGenerationRequest,
  BlockElementTypeInfo,
  BlockMappingSuggestion,
  SchemaTypeSuggestion,
  BulkSchemaTypeSuggestion,
} from '../api/types.js';

const API_BASE = '/umbraco/management/api/v1/schemeweaver';

const SESSION_EXPIRED_MESSAGE =
  'Your session has expired. Please reload the page and sign in again.';

/**
 * Thrown when a SchemeWeaver request comes back as the backoffice login page
 * instead of our JSON API — i.e. the user's session has timed out (a 401, a
 * redirect to login, or an HTML body). It is shaped like an Umbraco
 * `ProblemDetails` (type/title/status/detail) with status 401 so that
 * `tryExecute` recognises it as an auth error and suppresses its generic
 * "An error occurred" toast — we then surface a single friendly notification
 * ourselves. This avoids the raw "Unexpected token '<' in JSON" SyntaxError the
 * user would otherwise see.
 */
export class SchemeWeaverSessionExpiredError extends Error {
  readonly type = 'error';
  readonly title: string;
  readonly status = 401;
  readonly detail: string;

  constructor(message = SESSION_EXPIRED_MESSAGE) {
    super(message);
    this.name = 'SchemeWeaverSessionExpiredError';
    this.title = message;
    this.detail = message;
  }
}

/** True when a request failed because the backoffice session is no longer valid. */
function isUnauthenticated(error: unknown): boolean {
  // After tryExecute the original error is mapped to an UmbApiError that carries
  // the source ProblemDetails; a 401 there means "not authenticated".
  const status = (error as { problemDetails?: { status?: number } } | null)?.problemDetails?.status;
  return error instanceof SchemeWeaverSessionExpiredError || status === 401;
}

async function getNotificationContext(host: UmbControllerHost) {
  try {
    return await (host as ContextHost).getContext(UMB_NOTIFICATION_CONTEXT);
  } catch {
    return undefined;
  }
}

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
  expect404?: boolean,
): Promise<T | undefined> {
  const { data, error } = await tryExecute(
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

      // Legitimate "not found" (e.g. no mapping saved yet) — the caller opted in.
      if (expect404 && response.status === 404) {
        return { data: undefined as T };
      }

      // When the backoffice session times out the request is answered with the
      // HTML login page (often via a redirect) or a 401 rather than our JSON.
      // Detect that *before* parsing so we can show a clear "session expired"
      // message instead of a raw JSON-parse SyntaxError. Our API only ever
      // returns JSON (or 204), so a non-JSON body on an otherwise-OK response
      // means the login page was served.
      const contentType = (response.headers.get('content-type') ?? '').toLowerCase();
      const looksLikeLoginPage =
        response.status === 401 ||
        response.redirected ||
        (response.status !== 204 && !contentType.includes('json'));
      if (looksLikeLoginPage) {
        throw new SchemeWeaverSessionExpiredError();
      }

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
    // We render our own notifications below (tryExecute already suppresses 401s,
    // which is exactly the session-expiry case we want to message about).
    { disableNotifications: true },
  );

  if (error) {
    const notificationContext = await getNotificationContext(host);
    if (isUnauthenticated(error)) {
      notificationContext?.peek('warning', {
        data: { headline: 'Session expired', message: SESSION_EXPIRED_MESSAGE },
      });
    } else {
      notificationContext?.peek('danger', {
        data: { message: error instanceof Error ? error.message : 'An unexpected error occurred.' },
      });
    }
    return undefined;
  }

  return data;
}

export class SchemeWeaverServerDataSource {
  #host: UmbControllerHost;

  constructor(host: UmbControllerHost) {
    this.#host = host;
  }

  async getSchemaTypes(search?: string): Promise<SchemaTypeInfo[] | undefined> {
    const query = search ? `?search=${encodeURIComponent(search)}` : '';
    return fetchApi<SchemaTypeInfo[]>(this.#host, `/schema-types${query}`);
  }

  getSchemaTypeProperties(name: string): Promise<SchemaPropertyInfo[] | undefined>;
  getSchemaTypeProperties(name: string, ranked: true): Promise<RankedSchemaPropertyInfo[] | undefined>;
  async getSchemaTypeProperties(
    name: string,
    ranked?: boolean,
  ): Promise<SchemaPropertyInfo[] | RankedSchemaPropertyInfo[] | undefined> {
    const query = ranked ? '?ranked=true' : '';
    return fetchApi<SchemaPropertyInfo[] | RankedSchemaPropertyInfo[]>(
      this.#host,
      `/schema-types/${encodeURIComponent(name)}/properties${query}`,
    );
  }

  async getContentTypes(): Promise<ContentTypeInfo[] | undefined> {
    return fetchApi<ContentTypeInfo[]>(this.#host, '/content-types');
  }

  async getContentTypeProperties(alias: string): Promise<ContentTypeProperty[] | undefined> {
    return fetchApi<ContentTypeProperty[]>(
      this.#host,
      `/content-types/${encodeURIComponent(alias)}/properties`,
    );
  }

  async getMappings(): Promise<SchemaMappingDto[] | undefined> {
    return fetchApi<SchemaMappingDto[]>(this.#host, '/mappings');
  }

  async getMapping(contentTypeAlias: string): Promise<SchemaMappingDto | undefined> {
    return fetchApi<SchemaMappingDto>(
      this.#host,
      `/mappings/${encodeURIComponent(contentTypeAlias)}`,
      {},
      true,
    );
  }

  async saveMapping(dto: SchemaMappingDto): Promise<SchemaMappingDto | undefined> {
    return fetchApi<SchemaMappingDto>(this.#host, '/mappings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto),
    });
  }

  async deleteMapping(contentTypeAlias: string): Promise<void> {
    await fetchApi<void>(
      this.#host,
      `/mappings/${encodeURIComponent(contentTypeAlias)}`,
      { method: 'DELETE' },
    );
  }

  /** Returns flat array of PropertyMappingSuggestion (not wrapped) */
  async autoMap(
    contentTypeAlias: string,
    schemaTypeName: string,
  ): Promise<PropertyMappingSuggestion[] | undefined> {
    return fetchApi<PropertyMappingSuggestion[]>(
      this.#host,
      `/mappings/${encodeURIComponent(contentTypeAlias)}/auto-map?schemaTypeName=${encodeURIComponent(schemaTypeName)}`,
      { method: 'POST' },
    );
  }

  async preview(
    contentTypeAlias: string,
    contentKey?: string,
    culture?: string,
  ): Promise<JsonLdPreviewResponse | undefined> {
    const params = new URLSearchParams();
    if (contentKey) params.set('contentKey', contentKey);
    if (culture) params.set('culture', culture);
    const query = params.toString() ? `?${params.toString()}` : '';
    return fetchApi<JsonLdPreviewResponse>(
      this.#host,
      `/mappings/${encodeURIComponent(contentTypeAlias)}/preview${query}`,
      { method: 'POST' },
      true,
    );
  }

  async getBlockElementTypes(
    contentTypeAlias: string,
    propertyAlias: string,
  ): Promise<BlockElementTypeInfo[] | undefined> {
    return fetchApi<BlockElementTypeInfo[]>(
      this.#host,
      `/content-types/${encodeURIComponent(contentTypeAlias)}/properties/${encodeURIComponent(propertyAlias)}/block-types`,
    );
  }

  /**
   * Suggest routed block mappings for a block-list property. Returns one
   * suggestion per TARGET page property (mainEntity / hasPart / about / …),
   * each carrying the block-element routes that feed it. When
   * `targetSchemaProperty` is given, every top-level route is annotated with
   * `fitsTarget` — whether its nested type is range-assignable to that property
   * (server-side subtype walk; the client cannot compute this).
   */
  async suggestBlockMappings(
    contentTypeAlias: string,
    propertyAlias: string,
    targetSchemaProperty?: string,
  ): Promise<BlockMappingSuggestion[] | undefined> {
    const query = targetSchemaProperty
      ? `?targetSchemaProperty=${encodeURIComponent(targetSchemaProperty)}`
      : '';
    return fetchApi<BlockMappingSuggestion[]>(
      this.#host,
      `/content-types/${encodeURIComponent(contentTypeAlias)}/properties/${encodeURIComponent(propertyAlias)}/block-suggest${query}`,
      { method: 'POST' },
    );
  }

  async generateContentType(
    request: ContentTypeGenerationRequest,
  ): Promise<{ key: string } | undefined> {
    return fetchApi<{ key: string }>(this.#host, '/generate-content-type', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
  }

  // --- AI endpoints (require SchemeWeaver.AI satellite package) ---

  /** Returns { available: true } if AI satellite is installed, undefined if not (404). */
  async getAIStatus(): Promise<{ available: boolean } | undefined> {
    return fetchApi<{ available: boolean }>(this.#host, '/ai/status', {}, true);
  }

  /** AI-powered schema type suggestions for a single content type. */
  async suggestSchemaType(contentTypeAlias: string): Promise<SchemaTypeSuggestion[] | undefined> {
    return fetchApi<SchemaTypeSuggestion[]>(
      this.#host,
      `/ai/suggest-schema-type/${encodeURIComponent(contentTypeAlias)}`,
      { method: 'POST' },
    );
  }

  /** AI-powered schema type suggestions for all content types (bulk). */
  async suggestSchemaTypesBulk(): Promise<BulkSchemaTypeSuggestion[] | undefined> {
    return fetchApi<BulkSchemaTypeSuggestion[]>(
      this.#host,
      '/ai/suggest-schema-types-bulk',
      { method: 'POST' },
    );
  }

  /** AI-powered property mapping suggestions (returns same format as heuristic auto-map). */
  async aiAutoMap(
    contentTypeAlias: string,
    schemaTypeName: string,
  ): Promise<PropertyMappingSuggestion[] | undefined> {
    return fetchApi<PropertyMappingSuggestion[]>(
      this.#host,
      `/ai/ai-auto-map/${encodeURIComponent(contentTypeAlias)}?schemaTypeName=${encodeURIComponent(schemaTypeName)}`,
      { method: 'POST' },
    );
  }
}
