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
    Task<IEnumerable<PropertyMappingSuggestion>> AutoMapAsync(string contentTypeAlias, string schemaTypeName);
    JsonLdPreviewResponse GeneratePreview(IPublishedContent content, string? culture = null);
    JsonLdPreviewResponse GenerateMockPreview(string contentTypeAlias);

    /// <summary>
    /// Real preview for a single nested block instance on a page (located by
    /// <paramref name="blockInstanceKey"/>), rendered through the page mapping's route for that
    /// block type. The response carries an <c>info</c> issue naming the page node it resolved from.
    /// </summary>
    JsonLdPreviewResponse GenerateBlockInstancePreview(IPublishedContent page, Guid blockInstanceKey, string? culture = null);
    IEnumerable<SchemaTypeInfo> GetSchemaTypes();
    IEnumerable<SchemaTypeInfo> SearchSchemaTypes(string query);
    IEnumerable<SchemaPropertyInfo> GetSchemaProperties(string typeName);
    IEnumerable<SchemaMappingDto> GetAllMappings();
    Task<IEnumerable<BlockElementTypeInfo>> GetBlockElementTypesAsync(string contentTypeAlias, string propertyAlias);

    /// <summary>
    /// Suggests routed block mappings for a BlockList/BlockGrid property: each block
    /// element type is matched to a best-fit Schema.org type and target page property,
    /// grouped one suggestion per target property.
    /// </summary>
    Task<IEnumerable<BlockMappingSuggestion>> SuggestBlockMappingsAsync(string contentTypeAlias, string propertyAlias);
}
