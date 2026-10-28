using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Templates;

namespace Umbraco.Community.SchemeWeaver.Seo.Services;

/// <summary>
/// The projection between a template's slots and ordinary SchemeWeaver property-mapping rows.
/// The SEO edition never has a model of its own: it reads and writes the same rows the developer
/// tab does, so both UIs always agree and the generator, validator and MCP are untouched.
/// </summary>
public interface ISeoSlotProjector
{
    /// <summary>
    /// Views <paramref name="existing"/> (may be null) through <paramref name="template"/>, filling empty
    /// slots from <paramref name="suggestions"/> (auto-map output) and then the slot defaults.
    /// Pass <c>null</c> suggestions for a lossless view of what is stored (no defaults applied).
    /// </summary>
    SeoProjectionDto Project(
        SeoTemplate template,
        string schemaTypeName,
        SchemaMappingDto? existing,
        IReadOnlyList<PropertyMappingSuggestion>? suggestions,
        IReadOnlyDictionary<string, string>? editorAliasesByProperty,
        bool siteIdentityConfigured);

    /// <summary>
    /// Builds the mapping to save: one row per filled slot, every row the template does not
    /// own preserved verbatim from <paramref name="existing"/>.
    /// </summary>
    SchemaMappingDto ToMappingDto(
        SeoTemplate template,
        string contentTypeAlias,
        Guid contentTypeKey,
        string schemaTypeName,
        SeoMappingSaveRequest request,
        SchemaMappingDto? existing,
        IReadOnlyDictionary<string, string>? editorAliasesByProperty);

    /// <summary>Counts how many required / recommended slots a mapping fills (status rows, health).</summary>
    SeoMappingSummaryDto Summarise(SeoTemplate template, SchemaMappingDto mapping);
}
