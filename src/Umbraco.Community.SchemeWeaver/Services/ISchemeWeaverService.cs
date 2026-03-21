using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Orchestrator service combining registry, auto-mapper, generator, and repository.
/// </summary>
public interface ISchemeWeaverService
{
    SchemaMappingDto? GetMapping(string contentTypeAlias);
    SchemaMappingDto SaveMapping(SchemaMappingDto dto);
    void DeleteMapping(string contentTypeAlias);
    IEnumerable<PropertyMappingSuggestion> AutoMap(string contentTypeAlias, string schemaTypeName);
    JsonLdPreviewResponse GeneratePreview(IPublishedContent content);
    IEnumerable<SchemaTypeInfo> GetSchemaTypes();
    IEnumerable<SchemaTypeInfo> SearchSchemaTypes(string query);
    IEnumerable<SchemaPropertyInfo> GetSchemaProperties(string typeName);
    IEnumerable<SchemaMappingDto> GetAllMappings();
    Task<IEnumerable<BlockElementTypeInfo>> GetBlockElementTypesAsync(string contentTypeAlias, string propertyAlias);
}
