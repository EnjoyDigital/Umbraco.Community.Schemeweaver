import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import type {
  SchemaTypeInfo,
  SchemaPropertyInfo,
  ContentTypeInfo,
  ContentTypeProperty,
  SchemaMappingDto,
  PropertyMappingSuggestion,
  JsonLdPreviewResponse,
  ContentTypeGenerationRequest,
} from '../api/types.js';

const API_BASE = '/umbraco/management/api/v1/schemeweaver';

async function fetchApi<T>(
  host: UmbControllerHost,
  path: string,
  options: RequestInit = {},
): Promise<T | undefined> {
  const { data } = await tryExecute(
    host,
    (async () => {
      const response = await fetch(`${API_BASE}${path}`, {
        ...options,
        headers: {
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

export class SchemeWeaverServerDataSource {
  #host: UmbControllerHost;

  constructor(host: UmbControllerHost) {
    this.#host = host;
  }

  async getSchemaTypes(search?: string): Promise<SchemaTypeInfo[] | undefined> {
    const query = search ? `?search=${encodeURIComponent(search)}` : '';
    return fetchApi<SchemaTypeInfo[]>(this.#host, `/schema-types${query}`);
  }

  async getSchemaTypeProperties(name: string): Promise<SchemaPropertyInfo[] | undefined> {
    return fetchApi<SchemaPropertyInfo[]>(this.#host, `/schema-types/${encodeURIComponent(name)}/properties`);
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
  ): Promise<JsonLdPreviewResponse | undefined> {
    const query = contentKey ? `?contentKey=${encodeURIComponent(contentKey)}` : '';
    return fetchApi<JsonLdPreviewResponse>(
      this.#host,
      `/mappings/${encodeURIComponent(contentTypeAlias)}/preview${query}`,
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
}
