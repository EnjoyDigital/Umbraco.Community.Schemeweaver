using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Graph;

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
    string? GenerateGraphJson(IPublishedContent content, string? culture = null);
}
