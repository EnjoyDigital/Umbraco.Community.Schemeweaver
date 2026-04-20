using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;

namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// Resolves the site-level settings content node.
///
/// Lookup order:
///   1. <see cref="SiteSettingsOptions.ContentKey"/> if set — direct GetById lookup.
///   2. <see cref="IDocumentNavigationQueryService.TryGetRootKeysOfType"/> for the
///      configured content type alias (default <c>schemaSiteSettings</c>).
///   3. Descendants of every root — covers cases where settings live a couple
///      of levels deep (e.g. under a "Site Configuration" folder).
///
/// Returns null when no match is found; pieces that depend on the settings node
/// then skip themselves and the graph degrades to per-page content.
/// </summary>
public sealed class SiteSettingsResolver : ISiteSettingsResolver
{
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IDocumentNavigationQueryService _navigationQueryService;
    private readonly SchemeWeaverOptions _options;
    private readonly ILogger<SiteSettingsResolver> _logger;

    public SiteSettingsResolver(
        IUmbracoContextAccessor umbracoContextAccessor,
        IDocumentNavigationQueryService navigationQueryService,
        IOptions<SchemeWeaverOptions> options,
        ILogger<SiteSettingsResolver> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _navigationQueryService = navigationQueryService;
        _options = options.Value;
        _logger = logger;
    }

    public IPublishedContent? Resolve()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
            return null;

        var contentCache = umbracoContext.Content;
        if (contentCache is null)
            return null;

        var settings = _options.SiteSettings;

        // Explicit key wins — fast, unambiguous, no navigation lookup needed.
        if (settings.ContentKey is { } key && key != Guid.Empty)
        {
            var byKey = contentCache.GetById(key);
            if (byKey is not null)
                return byKey;

            _logger.LogDebug(
                "SiteSettings.ContentKey {Key} is configured but resolved no published content",
                key);
        }

        var alias = settings.ContentTypeAlias;
        if (string.IsNullOrWhiteSpace(alias))
            return null;

        // First try roots of the configured type.
        if (_navigationQueryService.TryGetRootKeysOfType(alias, out var rootMatches))
        {
            var first = FirstPublished(rootMatches, contentCache);
            if (first is not null)
                return first;
        }

        // Fall back to descendants of every root.
        if (_navigationQueryService.TryGetRootKeys(out var allRoots))
        {
            foreach (var rootKey in allRoots)
            {
                if (_navigationQueryService.TryGetDescendantsKeysOfType(rootKey, alias, out var descendants))
                {
                    var found = FirstPublished(descendants, contentCache);
                    if (found is not null)
                        return found;
                }
            }
        }

        return null;
    }

    private static IPublishedContent? FirstPublished(
        IEnumerable<Guid> keys,
        Umbraco.Cms.Core.PublishedCache.IPublishedContentCache cache)
    {
        foreach (var key in keys)
        {
            var content = cache.GetById(key);
            if (content is not null)
                return content;
        }
        return null;
    }
}
