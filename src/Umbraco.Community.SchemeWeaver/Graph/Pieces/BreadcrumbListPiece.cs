using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.Graph.Pieces;

/// <summary>
/// Emits the BreadcrumbList for the current page. Logic matches the pre-v1.4
/// <c>JsonLdGenerator.GenerateBreadcrumbJsonLd</c> — walks the parent chain,
/// reverses to root-first order, emits a ListItem per node with absolute URLs.
///
/// @id convention: <c>{pageUrl}#breadcrumb</c>. Skipped when the page has no
/// ancestors (breadcrumbs with a single item aren't meaningful).
/// </summary>
public sealed class BreadcrumbListPiece : IGraphPiece
{
    private readonly IDocumentNavigationQueryService _navigationQueryService;
    private readonly IPublishedContentStatusFilteringService _publishedStatusFilteringService;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly ILogger<BreadcrumbListPiece> _logger;

    public BreadcrumbListPiece(
        IDocumentNavigationQueryService navigationQueryService,
        IPublishedContentStatusFilteringService publishedStatusFilteringService,
        IPublishedUrlProvider urlProvider,
        ILogger<BreadcrumbListPiece> logger)
    {
        _navigationQueryService = navigationQueryService;
        _publishedStatusFilteringService = publishedStatusFilteringService;
        _urlProvider = urlProvider;
        _logger = logger;
    }

    public string Key => "breadcrumb";
    public int Order => 400;

    public string? ResolveId(GraphPieceContext ctx)
    {
        if (ctx.PageUrl is null)
            return null;
        var ancestors = WalkAncestors(ctx.Content);
        return ancestors.Count < 2 ? null : $"{ctx.PageUrl}#breadcrumb";
    }

    public Thing? Build(GraphPieceContext ctx)
    {
        var ancestors = WalkAncestors(ctx.Content);
        if (ancestors.Count < 2)
            return null;

        var breadcrumb = new BreadcrumbList();
        var items = new List<IListItem>(ancestors.Count);

        for (var i = 0; i < ancestors.Count; i++)
        {
            var ancestor = ancestors[i];
            var listItem = new ListItem
            {
                Position = i + 1,
                Name = ancestor.Name
            };

            var url = _urlProvider.GetUrl(ancestor, UrlMode.Absolute);
            if (!string.IsNullOrEmpty(url) && url != "#"
                && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                listItem.Url = uri;
                listItem.Id = uri;
            }

            items.Add(listItem);
        }

        breadcrumb.ItemListElement = items;
        return breadcrumb;
    }

    private List<IPublishedContent> WalkAncestors(IPublishedContent content)
    {
        var ancestors = new List<IPublishedContent> { content };
        try
        {
            var current = content.Parent<IPublishedContent>(
                _navigationQueryService,
                _publishedStatusFilteringService);
            while (current is not null)
            {
                ancestors.Add(current);
                current = current.Parent<IPublishedContent>(
                    _navigationQueryService,
                    _publishedStatusFilteringService);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to walk parent chain for content {ContentId}; breadcrumb skipped",
                content.Id);
            return [content];
        }
        ancestors.Reverse();
        return ancestors;
    }

}
