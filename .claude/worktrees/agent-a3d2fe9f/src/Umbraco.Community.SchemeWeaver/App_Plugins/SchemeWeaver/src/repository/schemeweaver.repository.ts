import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { SchemeWeaverServerDataSource } from './schemeweaver.server-data-source.js';
import type {
  SchemaMappingDto,
  ContentTypeGenerationRequest,
} from '../api/types.js';

export class SchemeWeaverRepository extends UmbControllerBase {
  #dataSource: SchemeWeaverServerDataSource;

  constructor(host: UmbControllerHost) {
    super(host);
    this.#dataSource = new SchemeWeaverServerDataSource(host);
  }

  async requestSchemaTypes(search?: string) {
    return this.#dataSource.getSchemaTypes(search);
  }

  async requestSchemaTypeProperties(name: string) {
    return this.#dataSource.getSchemaTypeProperties(name);
  }

  async requestContentTypes() {
    return this.#dataSource.getContentTypes();
  }

  async requestContentTypeProperties(alias: string) {
    return this.#dataSource.getContentTypeProperties(alias);
  }

  async requestMappings() {
    return this.#dataSource.getMappings();
  }

  async requestMapping(contentTypeAlias: string) {
    return this.#dataSource.getMapping(contentTypeAlias);
  }

  async saveMapping(dto: SchemaMappingDto) {
    return this.#dataSource.saveMapping(dto);
  }

  async deleteMapping(contentTypeAlias: string) {
    return this.#dataSource.deleteMapping(contentTypeAlias);
  }

  async requestAutoMap(contentTypeAlias: string, schemaTypeName: string) {
    return this.#dataSource.autoMap(contentTypeAlias, schemaTypeName);
  }

  async requestPreview(contentTypeAlias: string, contentKey?: string) {
    return this.#dataSource.preview(contentTypeAlias, contentKey);
  }

  async generateContentType(request: ContentTypeGenerationRequest) {
    return this.#dataSource.generateContentType(request);
  }

  /** Resolve a document type GUID (unique) to its content type alias */
  async resolveContentTypeAlias(unique: string): Promise<string | undefined> {
    const contentTypes = await this.#dataSource.getContentTypes();
    if (!contentTypes) return undefined;
    const match = contentTypes.find((ct) => ct.key === unique);
    return match?.alias;
  }
}
