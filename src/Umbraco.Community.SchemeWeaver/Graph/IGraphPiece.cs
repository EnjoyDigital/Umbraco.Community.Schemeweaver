using Schema.NET;

namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// Where a piece belongs in the site-vs-page emission split. Headless frontends
/// commonly emit site-level JSON-LD from their root layout (same on every
/// page) and page-level JSON-LD from the page body (varies per route). Pieces
/// declare their scope so callers can request "site only" or "page only"
/// graphs via the Delivery API's <c>?scope=</c> query parameter.
/// </summary>
public enum PieceScope
{
    /// <summary>
    /// Page-scoped piece — describes the current content. Emitted per route
    /// (WebPage, Breadcrumb, PrimaryImage, main entity). Default for custom
    /// pieces because most describe the current page.
    /// </summary>
    Page = 0,

    /// <summary>
    /// Site-scoped piece — describes the whole site. Emitted once per site,
    /// constant across routes (Organization, WebSite). A headless frontend
    /// typically fetches these once from the root layout.
    /// </summary>
    Site = 1,
}

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
    /// Where this piece belongs in the site-vs-page emission split. See
    /// <see cref="PieceScope"/>. Defaults to <see cref="PieceScope.Page"/>
    /// so extension-registered pieces describing the current content get the
    /// expected behaviour without having to think about scope.
    /// </summary>
    PieceScope Scope => PieceScope.Page;

    /// <summary>
    /// First pass of the two-phase assembly. Returns the absolute @id this
    /// piece will emit, or <c>null</c> if the piece isn't needed for this
    /// request (e.g. no site-settings content node resolved, or the current
    /// page has no mapping). Pieces that return null are skipped entirely —
    /// they don't appear in the graph and their key is absent from
    /// <see cref="GraphPieceContext.Ids"/>.
    ///
    /// Note: <c>ResolveId</c> runs regardless of the request's scope filter so
    /// <see cref="GraphPieceContext.Ids"/> contains every piece's @id — a
    /// <c>scope=page</c> request's WebPage can still emit
    /// <c>publisher: {"@id": "...#organization"}</c> and the consumer's
    /// separate site-scope script tag supplies the Organization body.
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
