using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// Which subset of <see cref="IGraphPiece"/>s to emit. Used by headless
/// frontends to split JSON-LD between a root layout (<see cref="Site"/>) and
/// the page body (<see cref="Page"/>) without double-emitting site-level
/// entities on every route.
/// </summary>
public enum PieceScopeFilter
{
    /// <summary>Emit every needed piece regardless of scope (default, v1.4 behaviour).</summary>
    All = 0,

    /// <summary>Emit only pieces whose <see cref="IGraphPiece.Scope"/> is <see cref="PieceScope.Site"/>.</summary>
    Site = 1,

    /// <summary>Emit only pieces whose <see cref="IGraphPiece.Scope"/> is <see cref="PieceScope.Page"/>.</summary>
    Page = 2,
}

/// <summary>
/// Composes a single JSON-LD @graph from the registered <see cref="IGraphPiece"/>
/// implementations. Produces the exact shape a Yoast-style front-end expects:
/// one &lt;script type="application/ld+json"&gt; body, top-level @context,
/// one @graph array with cross-referenced nodes.
/// </summary>
public interface IGraphGenerator
{
    /// <summary>
    /// Build the graph as a serialised JSON-LD string, ready to drop into a
    /// &lt;script&gt; tag or a Delivery API payload. Returns null when no
    /// pieces are needed for the request (e.g. current page has no mapping
    /// and no site-level pieces apply).
    /// </summary>
    /// <param name="scope">
    /// Optional scope filter for headless frontends. Defaults to
    /// <see cref="PieceScopeFilter.All"/> (back-compat with v1.4). Cross-piece
    /// <c>@id</c> references resolve regardless of scope — a <c>Page</c>-scoped
    /// graph can still reference the Organization's @id, which its sibling
    /// <c>Site</c>-scoped graph supplies in a separate script tag.
    /// </param>
    string? GenerateGraphJson(
        IPublishedContent content,
        string? culture = null,
        PieceScopeFilter scope = PieceScopeFilter.All);
}
