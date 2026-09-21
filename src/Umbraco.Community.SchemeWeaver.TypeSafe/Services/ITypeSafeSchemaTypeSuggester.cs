namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services;

/// <summary>
/// Suggests the Schema.org type for a content type by hierarchical classification: a beam
/// search from <c>Thing</c> down the type tree, one Choice per level with a "stop here"
/// option, ranked by geometric-mean edge probability so shallow and deep landing points
/// compare fairly. Replaces guessing from a substring search over type names.
/// </summary>
public interface ITypeSafeSchemaTypeSuggester
{
    /// <summary>
    /// Returns up to <paramref name="maxResults"/> candidate types, best first. Empty when the
    /// content type is unknown. Throws on an API failure — the caller decides the fallback.
    /// </summary>
    Task<IReadOnlyList<TypeSafeSchemaTypeSuggestion>> SuggestAsync(
        string contentTypeAlias,
        int maxResults = 3,
        CancellationToken cancellationToken = default);
}

/// <summary>One candidate Schema.org type for a content type.</summary>
/// <param name="SchemaTypeName">The type, e.g. <c>BlogPosting</c>.</param>
/// <param name="Confidence">Calibrated 0–100, from the beam's geometric-mean edge probability.</param>
/// <param name="Path">The descent that reached it, root first, e.g. <c>Thing › CreativeWork › Article › BlogPosting</c>.</param>
public sealed record TypeSafeSchemaTypeSuggestion(
    string SchemaTypeName,
    int Confidence,
    IReadOnlyList<string> Path);
