using Microsoft.Extensions.Caching.Memory;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;

namespace Umbraco.Community.SchemeWeaver.Seo.Services;

public sealed class PublishedContentOfTypeQuery : IPublishedContentOfTypeQuery
{
    private static readonly TimeSpan CountCacheDuration = TimeSpan.FromSeconds(60);

    private readonly IDocumentNavigationQueryService _navigation;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IMemoryCache _cache;

    public PublishedContentOfTypeQuery(
        IDocumentNavigationQueryService navigation,
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoContextFactory umbracoContextFactory,
        IMemoryCache cache)
    {
        _navigation = navigation;
        _umbracoContextAccessor = umbracoContextAccessor;
        _umbracoContextFactory = umbracoContextFactory;
        _cache = cache;
    }

    public int PublishedCount(string contentTypeAlias) =>
        _cache.GetOrCreate($"schemeweaver:seo:published-count:{contentTypeAlias}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CountCacheDuration;
            return Published(contentTypeAlias).Count;
        });

    public IPublishedContent? MostRecentlyPublished(string contentTypeAlias) =>
        RecentlyPublished(contentTypeAlias, 1).FirstOrDefault();

    public IReadOnlyList<IPublishedContent> RecentlyPublished(string contentTypeAlias, int take) =>
        Published(contentTypeAlias)
            .OrderByDescending(c => c.UpdateDate)
            .Take(Math.Max(1, take))
            .ToList();

    private List<IPublishedContent> Published(string contentTypeAlias)
    {
        var keys = new HashSet<Guid>();
        if (_navigation.TryGetRootKeysOfType(contentTypeAlias, out var rootKeys))
            keys.UnionWith(rootKeys);

        if (_navigation.TryGetRootKeys(out var allRoots))
        {
            foreach (var root in allRoots)
            {
                if (_navigation.TryGetDescendantsKeysOfType(root, contentTypeAlias, out var descendants))
                    keys.UnionWith(descendants);
            }
        }

        if (keys.Count == 0)
            return [];

        if (_umbracoContextAccessor.TryGetUmbracoContext(out var current) && current.Content is not null)
            return Resolve(current.Content, keys);

        using var reference = _umbracoContextFactory.EnsureUmbracoContext();
        return reference.UmbracoContext.Content is { } cache ? Resolve(cache, keys) : [];
    }

    private static List<IPublishedContent> Resolve(Umbraco.Cms.Core.PublishedCache.IPublishedContentCache cache, IEnumerable<Guid> keys) =>
        keys.Select(cache.GetById).Where(c => c is not null).Select(c => c!).ToList();
}
