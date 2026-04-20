using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// Per-request state passed to every <see cref="IGraphPiece"/>. Built once by
/// <see cref="GraphGenerator"/> before the first pass, mutated between passes
/// to populate <see cref="Ids"/>, and read by pieces during
/// <see cref="IGraphPiece.Build"/> to cross-reference other pieces.
/// </summary>
public sealed class GraphPieceContext
{
    /// <summary>The page currently being rendered.</summary>
    public required IPublishedContent Content { get; init; }

    /// <summary>
    /// Site-level settings node resolved by convention (see
    /// <c>SchemeWeaverOptions.SiteSettings</c>). Null when no settings node is
    /// configured or found — pieces that depend on it should return null from
    /// <see cref="IGraphPiece.ResolveId"/>.
    /// </summary>
    public IPublishedContent? SiteSettings { get; init; }

    /// <summary>Requested culture, or null for invariant.</summary>
    public string? Culture { get; init; }

    /// <summary>Absolute site root URL (e.g. <c>https://example.com</c>). Null when unresolvable.</summary>
    public Uri? SiteUrl { get; init; }

    /// <summary>Absolute URL of the current page. Null when unresolvable.</summary>
    public Uri? PageUrl { get; init; }

    /// <summary>
    /// Piece key → resolved @id, populated by the generator between the two
    /// passes. During <see cref="IGraphPiece.ResolveId"/> this is empty;
    /// during <see cref="IGraphPiece.Build"/> it contains every needed piece's
    /// @id. Lookups are case-sensitive — keys are already normalised lowercase
    /// by <see cref="IGraphPiece.Key"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Ids { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Convenience accessor: returns the @id for the named piece, or null if
    /// that piece isn't in the graph. Use when building a cross-reference
    /// like <c>{"@id": "..."}</c> — the piece should check for null and
    /// omit the property if the referenced piece isn't present.
    /// </summary>
    public string? IdFor(string pieceKey) =>
        Ids.TryGetValue(pieceKey, out var id) ? id : null;
}
