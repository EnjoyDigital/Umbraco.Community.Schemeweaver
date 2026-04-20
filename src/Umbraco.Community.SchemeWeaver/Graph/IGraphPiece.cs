using Schema.NET;

namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// A single named contributor to the @graph emitted for a page. Pieces are the
/// SchemeWeaver analogue of Yoast's "schema pieces" — Organization, WebSite,
/// WebPage, Breadcrumb, PrimaryImage, Author, and so on. Each piece owns its
/// own @id convention and decides per request whether it's needed. The
/// GraphGenerator composes all registered pieces into a single JSON-LD graph.
///
/// Implementations are registered via
/// <see cref="GraphServiceCollectionExtensions.AddSchemeWeaverGraphPiece{T}"/>
/// and must be stateless / safe to resolve per request.
/// </summary>
public interface IGraphPiece
{
    /// <summary>
    /// Stable, lowercase identifier (e.g. <c>"organization"</c>, <c>"webpage"</c>).
    /// Used as the key in <see cref="GraphPieceContext.Ids"/> so pieces can cross-reference
    /// each other's @id without knowing the concrete implementation.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Sort order for emission. Doesn't affect semantics (consumers work from
    /// @id references, not array position), but a stable order keeps generated
    /// output diff-friendly. Built-in pieces use 100-spaced numbers so custom
    /// pieces can slot between them.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// First pass of the two-phase assembly. Returns the absolute @id this
    /// piece will emit, or <c>null</c> if the piece isn't needed for this
    /// request (e.g. no site-settings content node resolved, or the current
    /// page has no mapping). Pieces that return null are skipped entirely —
    /// they don't appear in the graph and their key is absent from
    /// <see cref="GraphPieceContext.Ids"/>.
    /// </summary>
    string? ResolveId(GraphPieceContext ctx);

    /// <summary>
    /// Second pass. Builds the Schema.NET Thing for this piece. At this point
    /// <see cref="GraphPieceContext.Ids"/> contains every needed piece's @id,
    /// so cross-references can be constructed. Return <c>null</c> to skip the
    /// piece after all (e.g. data turned out to be incomplete).
    /// </summary>
    Thing? Build(GraphPieceContext ctx);
}
