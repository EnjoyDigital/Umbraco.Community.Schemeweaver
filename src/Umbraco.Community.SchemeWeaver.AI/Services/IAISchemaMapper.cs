using Umbraco.Community.SchemeWeaver.AI.Models;
using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.AI.Services;

/// <summary>
/// Uses AI to suggest Schema.org types and property mappings for Umbraco content types.
/// </summary>
public interface IAISchemaMapper
{
    /// <summary>
    /// Suggests the best Schema.org types for a single content type.
    /// </summary>
    Task<SchemaTypeSuggestion[]> SuggestSchemaTypesAsync(
        string contentTypeAlias, CancellationToken ct = default);

    /// <summary>
    /// Suggests Schema.org types for all content types in one batch.
    /// </summary>
    Task<BulkSchemaTypeSuggestion[]> SuggestSchemaTypesForAllAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Uses AI to suggest property mappings between a content type and a Schema.org type.
    /// Falls back to heuristic mappings on failure.
    /// </summary>
    Task<PropertyMappingSuggestion[]> SuggestPropertyMappingsAsync(
        string contentTypeAlias, string schemaTypeName, CancellationToken ct = default);
}
