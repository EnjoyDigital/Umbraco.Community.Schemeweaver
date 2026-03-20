import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbObjectState, UmbArrayState } from '@umbraco-cms/backoffice/observable-api';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import { SCHEMEWEAVER_CONTEXT } from './schemeweaver.context-token.js';
import type {
  SchemaTypeInfo,
  ContentTypeInfo,
  SchemaMappingDto,
  JsonLdPreviewResponse,
  ContentTypeGenerationRequest,
} from '../api/types.js';

export class SchemeWeaverContext extends UmbControllerBase {
  #repository: SchemeWeaverRepository;

  #schemaTypes = new UmbArrayState<SchemaTypeInfo>([], (x) => x.name);
  public readonly schemaTypes = this.#schemaTypes.asObservable();

  #contentTypes = new UmbArrayState<ContentTypeInfo>([], (x) => x.alias);
  public readonly contentTypes = this.#contentTypes.asObservable();

  #mappings = new UmbArrayState<SchemaMappingDto>([], (x) => x.contentTypeAlias);
  public readonly mappings = this.#mappings.asObservable();

  #currentMapping = new UmbObjectState<SchemaMappingDto | undefined>(undefined);
  public readonly currentMapping = this.#currentMapping.asObservable();

  #preview = new UmbObjectState<JsonLdPreviewResponse | undefined>(undefined);
  public readonly preview = this.#preview.asObservable();

  #loading = new UmbObjectState<boolean>(false);
  public readonly loading = this.#loading.asObservable();

  constructor(host: UmbControllerHost) {
    super(host);
    this.#repository = new SchemeWeaverRepository(host);
    this.provideContext(SCHEMEWEAVER_CONTEXT, this);
  }

  async loadSchemaTypes(search?: string) {
    const result = await this.#repository.requestSchemaTypes(search);
    if (result) {
      this.#schemaTypes.setValue(result);
    }
  }

  async loadContentTypes() {
    const result = await this.#repository.requestContentTypes();
    if (result) {
      this.#contentTypes.setValue(result);
    }
  }

  async loadMappings() {
    this.#loading.setValue(true);
    try {
      const result = await this.#repository.requestMappings();
      if (result) {
        this.#mappings.setValue(result);
      }
    } finally {
      this.#loading.setValue(false);
    }
  }

  async loadMapping(contentTypeAlias: string) {
    this.#loading.setValue(true);
    try {
      const result = await this.#repository.requestMapping(contentTypeAlias);
      this.#currentMapping.setValue(result);
    } finally {
      this.#loading.setValue(false);
    }
  }

  async saveMapping(dto: SchemaMappingDto) {
    const result = await this.#repository.saveMapping(dto);
    if (result) {
      await this.loadMappings();
    }
    return result;
  }

  async deleteMapping(contentTypeAlias: string) {
    await this.#repository.deleteMapping(contentTypeAlias);
    await this.loadMappings();
  }

  async autoMap(contentTypeAlias: string, schemaTypeName: string) {
    return this.#repository.requestAutoMap(contentTypeAlias, schemaTypeName);
  }

  async requestPreview(contentTypeAlias: string, contentKey?: string) {
    const result = await this.#repository.requestPreview(contentTypeAlias, contentKey);
    if (result) {
      this.#preview.setValue(result);
    }
    return result;
  }

  async generateContentType(request: ContentTypeGenerationRequest) {
    return this.#repository.generateContentType(request);
  }
}
