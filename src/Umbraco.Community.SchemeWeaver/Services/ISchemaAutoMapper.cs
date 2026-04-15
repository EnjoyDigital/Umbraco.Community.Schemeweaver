using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Suggests property mappings between Umbraco content types and Schema.org types.
/// </summary>
public interface ISchemaAutoMapper
{
    IEnumerable<PropertyMappingSuggestion> SuggestMappings(string contentTypeAlias, string schemaTypeName);

    /// <summary>
    /// Returns the Schema.org properties for <paramref name="schemaTypeName"/> ranked by how likely
    /// they are to be useful as a nested-type mapping target, sorted by confidence desc then name asc.
    /// Returns empty when the type is unknown.
    /// </summary>
    IEnumerable<RankedSchemaPropertyInfo> RankSchemaProperties(string schemaTypeName);
}
