using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Graph;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Single source of truth for the ordered JSON-LD block array surfaced to the Delivery API
/// endpoint and the Examine content index. Caches per (content key, culture, scope);
/// invalidated by the notification handlers in <c>SchemeWeaver.Notifications</c>.
/// </summary>
public interface IJsonLdBlocksProvider
{
    /// <summary>
    /// Returns the ordered JSON-LD blocks for <paramref name="content"/> in
    /// <paramref name="culture"/> filtered by <paramref name="scope"/>. Order: inherited
    /// ancestor schemas (root-first) → <c>BreadcrumbList</c> (unless opted out) → main page
    /// schema → block element schemas (legacy path), or a single <c>@graph</c> element
    /// (graph-model path, the v1.4+ default). Empty array if there is nothing to emit.
    /// </summary>
    /// <param name="scope">
    /// Optional scope filter for headless frontends that split JSON-LD between layout (site)
    /// and page body (page). Defaults to <see cref="PieceScopeFilter.All"/> (back-compat).
    /// Ignored in legacy (non-graph-model) mode.
    /// </param>
    string[] GetBlocks(IPublishedContent content, string? culture, PieceScopeFilter scope = PieceScopeFilter.All);

    /// <summary>
    /// Evict the cache entries for a single content key across every culture.
    /// </summary>
    void Invalidate(Guid contentKey);

    /// <summary>
    /// Evict every cache entry. Use sparingly — triggered by schema mapping writes that could
    /// change the JSON-LD for any content in the tree.
    /// </summary>
    void InvalidateAll();
}
