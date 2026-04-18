using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Single source of truth for the ordered JSON-LD block array surfaced to the Delivery API
/// endpoint and the Examine content index. Caches per (content key, culture); invalidated by
/// the notification handlers in <c>SchemeWeaver.Notifications</c>.
/// </summary>
public interface IJsonLdBlocksProvider
{
    /// <summary>
    /// Returns the ordered JSON-LD blocks for <paramref name="content"/> in
    /// <paramref name="culture"/>. Order: inherited ancestor schemas (root-first) →
    /// <c>BreadcrumbList</c> (unless opted out) → main page schema → block element schemas.
    /// Empty array if there is nothing to emit.
    /// </summary>
    string[] GetBlocks(IPublishedContent content, string? culture);

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
