using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Suggests property mappings between Umbraco content types and Schema.org types.
/// </summary>
public interface ISchemaAutoMapper
{
    IEnumerable<PropertyMappingSuggestion> SuggestMappings(string contentTypeAlias, string schemaTypeName);
}
