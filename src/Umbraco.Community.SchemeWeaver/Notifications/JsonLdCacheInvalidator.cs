using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Shared eviction helper used by every content-lifecycle notification handler.
///
/// Always evicts the affected content's own cache entries. It then ripples to the whole cache ONLY
/// when other nodes can actually depend on the affected node's JSON-LD — i.e. the site uses
/// inherited schemas or cross-node source types (ancestor/parent/sibling/reference), the affected
/// node is the site-settings node (whose Organization/WebSite graph pieces are baked into EVERY
/// routed page's cached graph), or the event is a move (which changes the moved subtree's
/// ancestry/breadcrumbs). The ripple is a single O(1)
/// <see cref="IJsonLdBlocksProvider.InvalidateAll"/> rather than the previous per-publish
/// <c>IContentService.GetPagedDescendants</c> walk, which loaded the whole subtree's full
/// <see cref="IContent"/> from the DB inside the publish scope (under the SQLite ContentTree write
/// lock) — the cause of publishing stalling for tens of seconds. The dependency check reads the
/// cached <see cref="ISchemaMappingRepository"/>, so it costs no extra DB work.
/// </summary>
internal static class JsonLdCacheInvalidator
{
    public static void InvalidateTree(
        IJsonLdBlocksProvider provider,
        ISchemaMappingRepository mappingRepository,
        ILogger logger,
        IEnumerable<IContent> entities,
        SiteSettingsOptions siteSettings,
        bool alwaysRippleToDescendants = false)
    {
        var ripple = alwaysRippleToDescendants;
        var any = false;

        foreach (var content in entities.Where(c => c is not null))
        {
            provider.Invalidate(content.Key);
            any = true;

            // The site-settings node feeds the Site-scoped graph pieces (Organization, …), which
            // are cached under every ROUTED page's key — its own key holds nothing of interest.
            // Cheap alias/key comparison, so check it before the repository lookup.
            if (!ripple && IsSiteSettingsNode(siteSettings, content))
                ripple = true;

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

    /// <summary>
    /// Mirrors <see cref="Graph.SiteSettingsResolver"/>'s discovery logic: a node counts as the
    /// site-settings node when it matches the configured <see cref="SiteSettingsOptions.ContentKey"/>
    /// or carries the configured <see cref="SiteSettingsOptions.ContentTypeAlias"/>. Both are
    /// checked (the resolver falls back to the alias lookup when the key resolves nothing), and the
    /// alias comparison is case-insensitive — over-invalidating on a rare settings publish is far
    /// cheaper than serving a stale site graph until restart.
    /// </summary>
    private static bool IsSiteSettingsNode(SiteSettingsOptions siteSettings, IContent content)
    {
        if (siteSettings.ContentKey is { } key && key != Guid.Empty && content.Key == key)
            return true;

        return !string.IsNullOrWhiteSpace(siteSettings.ContentTypeAlias)
            && string.Equals(content.ContentType.Alias, siteSettings.ContentTypeAlias, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// A content-picker / MNTP row is <c>property</c>-sourced, so <see cref="SchemeWeaverConstants.SourceTypes.IsCrossNode"/>
    /// does not see it — but it renders data belonging to whatever node was PICKED, which means
    /// publishing that node has to evict the picking page's cached JSON-LD. Without this, editing an
    /// Author node left every article that picks it serving the pre-edit graph until republish.
    ///
    /// The mapping row does not record the property's editor alias, so this infers "renders another
    /// node" from the configuration only a picker row ever carries: a nested type to render the
    /// picked item as, or a picked-item resolver config (drill-down, or a per-usage object).
    /// </summary>
    private static bool IsPickedContentRow(Models.Entities.PropertyMapping mapping)
    {
        if (!string.Equals(mapping.SourceType, SchemeWeaverConstants.SourceTypes.Property,
                StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(mapping.NestedSchemaTypeName))
            return true;

        var picked = Services.Resolvers.PickedContentResolver.ParseConfig(mapping.ResolverConfig);
        return !string.IsNullOrWhiteSpace(picked?.PickedPropertyAlias)
            || !string.IsNullOrWhiteSpace(picked?.NestedSchemaTypeName);
    }

    private static bool SiteHasCrossNodeMappings(ISchemaMappingRepository repo, ILogger logger)
    {
        try
        {
            if (repo.GetInheritedMappings().Any())
                return true;

            return repo.GetAllPropertyMappingsByMappingId().Values
                .SelectMany(list => list)
                .Any(p => SchemeWeaverConstants.SourceTypes.IsCrossNode(p.SourceType)
                    || IsPickedContentRow(p));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to inspect mappings for cross-node dependencies; rippling JSON-LD invalidation.");
            return true;
        }
    }
}
