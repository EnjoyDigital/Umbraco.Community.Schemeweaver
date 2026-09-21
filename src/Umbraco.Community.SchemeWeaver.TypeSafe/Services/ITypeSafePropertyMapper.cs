using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services;

/// <summary>
/// Builds a property mapping for one content type out of TypeSafe judgments: bind each
/// content property to a schema property (Choice over the real property names), decide
/// the source type (Noul for entity-vs-value, Choice for block shape), beam-search the
/// nested type down the Schema.org tree from the property's declared range, then bind the
/// inner properties. The heuristic's suggestions are supplied as priors and merged by code.
/// </summary>
public interface ITypeSafePropertyMapper
{
    /// <summary>
    /// Suggests property mappings. <paramref name="priors"/> is the heuristic auto-mapper's
    /// output for the same pair; rows it is genuinely sure of (the Google-mandated shapes in
    /// its popular-defaults table, exact-alias matches) are kept, and TypeSafe supplies the
    /// rest with calibrated confidence in place of the heuristic's tier numbers. Throws on an
    /// API failure — the caller decides the fallback.
    /// </summary>
    Task<IReadOnlyList<PropertyMappingSuggestion>> MapAsync(
        string contentTypeAlias,
        string schemaTypeName,
        IReadOnlyList<PropertyMappingSuggestion> priors,
        CancellationToken cancellationToken = default);
}
