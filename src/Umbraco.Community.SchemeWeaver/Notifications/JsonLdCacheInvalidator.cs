using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Shared eviction helper used by every content-lifecycle notification handler.
/// Evicts the target content's cache entries in every culture, plus all descendants
/// (inherited schemas on an ancestor ripple down — a publish on Home needs to
/// invalidate every descendant so their inherited WebSite block picks up the change).
/// </summary>
internal static class JsonLdCacheInvalidator
{
    private const int PageSize = 200;

    public static void InvalidateTree(
        IJsonLdBlocksProvider provider,
        IContentService contentService,
        ILogger logger,
        IEnumerable<IContent> entities)
    {
        foreach (var content in entities)
        {
            if (content is null) continue;
            provider.Invalidate(content.Key);
            InvalidateDescendants(provider, contentService, logger, content.Id);
        }
    }

    private static void InvalidateDescendants(
        IJsonLdBlocksProvider provider,
        IContentService contentService,
        ILogger logger,
        int parentId)
    {
        try
        {
            long total;
            long page = 0;
            do
            {
                var descendants = contentService.GetPagedDescendants(parentId, page, PageSize, out total);
                foreach (var descendant in descendants)
                {
                    provider.Invalidate(descendant.Key);
                }
                page++;
            } while (page * PageSize < total);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to walk descendants when invalidating JSON-LD cache for content {ParentId}",
                parentId);
        }
    }
}
