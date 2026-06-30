using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Shared eviction helper used by every content-lifecycle notification handler.
///
/// Always evicts the affected content's own cache entries. It then ripples to descendants ONLY
/// when descendants can actually depend on the affected node's JSON-LD — i.e. the site uses
/// inherited schemas or cross-node source types (ancestor/parent/sibling/reference), or the event
/// is a move (which changes the moved subtree's ancestry/breadcrumbs). The ripple is a single O(1)
/// <see cref="IJsonLdBlocksProvider.InvalidateAll"/> rather than the previous per-publish
/// <c>IContentService.GetPagedDescendants</c> walk, which loaded the whole subtree's full
/// <see cref="IContent"/> from the DB inside the publish scope (under the SQLite ContentTree write
/// lock) — the cause of publishing stalling for tens of seconds. The dependency check reads the
/// cached <see cref="ISchemaMappingRepository"/>, so it costs no extra DB work.
/// </summary>
internal static class JsonLdCacheInvalidator
{
    private static readonly HashSet<string> CrossNodeSourceTypes =
        new(StringComparer.OrdinalIgnoreCase) { "ancestor", "parent", "sibling", "reference" };

    public static void InvalidateTree(
        IJsonLdBlocksProvider provider,
        ISchemaMappingRepository mappingRepository,
        ILogger logger,
        IEnumerable<IContent> entities,
        bool alwaysRippleToDescendants = false)
    {
        var ripple = alwaysRippleToDescendants;
        var any = false;

        foreach (var content in entities)
        {
            if (content is null) continue;
            provider.Invalidate(content.Key);
            any = true;

            // Descendants render THIS node's schema only when its mapping is inherited.
            if (!ripple && IsInheritedType(mappingRepository, content, logger))
                ripple = true;
        }

        if (!any)
            return;

        // A node with a cross-node source mapping (ancestor/parent/sibling/reference) pulls data
        // from some OTHER node; we can't cheaply tell which, so any such site invalidates broadly.
        if (!ripple && SiteHasCrossNodeMappings(mappingRepository, logger))
            ripple = true;

        if (ripple)
            provider.InvalidateAll();
    }

    private static bool IsInheritedType(ISchemaMappingRepository repo, IContent content, ILogger logger)
    {
        try
        {
            return repo.GetByContentTypeAlias(content.ContentType.Alias)?.IsInherited == true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve mapping for {Alias}; rippling JSON-LD invalidation to be safe.",
                content.ContentType.Alias);
            return true; // over-invalidate rather than risk stale JSON-LD
        }
    }

    private static bool SiteHasCrossNodeMappings(ISchemaMappingRepository repo, ILogger logger)
    {
        try
        {
            if (repo.GetInheritedMappings().Any())
                return true;

            return repo.GetAllPropertyMappingsByMappingId().Values
                .SelectMany(list => list)
                .Any(p => CrossNodeSourceTypes.Contains(p.SourceType));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to inspect mappings for cross-node dependencies; rippling JSON-LD invalidation.");
            return true;
        }
    }
}
