using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Seo.Services;

/// <summary>Published nodes of a content type, via the navigation service and the published cache (never IContentService counts).</summary>
public interface IPublishedContentOfTypeQuery
{
    int PublishedCount(string contentTypeAlias);

    /// <summary>The most recently updated published node of the type, or null.</summary>
    IPublishedContent? MostRecentlyPublished(string contentTypeAlias);

    IReadOnlyList<IPublishedContent> RecentlyPublished(string contentTypeAlias, int take);
}
