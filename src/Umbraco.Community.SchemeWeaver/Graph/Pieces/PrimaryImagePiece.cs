using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.Graph.Pieces;

/// <summary>
/// Emits an ImageObject for the current page's primary image. By convention
/// the first published property whose alias matches <c>primaryImage</c>,
/// <c>heroImage</c>, <c>image</c>, <c>featuredImage</c>, or <c>ogImage</c>
/// (case-insensitive) wins. @id is <c>{pageUrl}#primaryimage</c>.
///
/// Skipped when no match is found or the matched property yields no media.
/// Other pieces (e.g. WebPage → primaryImageOfPage) can reference the @id
/// via <see cref="GraphPieceContext.IdFor"/>.
/// </summary>
public sealed class PrimaryImagePiece : IGraphPiece
{
    private static readonly string[] _conventionAliases =
    [
        "primaryImage",
        "heroImage",
        "image",
        "featuredImage",
        "ogImage"
    ];

    private readonly IPublishedUrlProvider _urlProvider;
    private readonly ILogger<PrimaryImagePiece> _logger;

    public PrimaryImagePiece(
        IPublishedUrlProvider urlProvider,
        ILogger<PrimaryImagePiece> logger)
    {
        _urlProvider = urlProvider;
        _logger = logger;
    }

    public string Key => "primary-image";
    public int Order => 500;

    public string? ResolveId(GraphPieceContext ctx)
    {
        if (ctx.PageUrl is null)
            return null;
        return GetMediaProperty(ctx.Content) is null ? null : $"{ctx.PageUrl}#primaryimage";
    }

    public Thing? Build(GraphPieceContext ctx)
    {
        var media = GetMediaProperty(ctx.Content);
        if (media is null)
            return null;

        var url = _urlProvider.GetMediaUrl(media, UrlMode.Absolute);
        if (string.IsNullOrEmpty(url))
            return null;

        var image = new ImageObject();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            image.Url = uri;

        if (media.Value<int?>("umbracoWidth") is int width && width > 0)
            image.Width = new QuantitativeValue { Value = width };
        if (media.Value<int?>("umbracoHeight") is int height && height > 0)
            image.Height = new QuantitativeValue { Value = height };

        return image;
    }

    private IPublishedContent? GetMediaProperty(IPublishedContent content)
    {
        foreach (var alias in _conventionAliases)
        {
            try
            {
                var value = content.Value(alias);
                if (value is IPublishedContent single)
                    return single;
                if (value is IEnumerable<IPublishedContent> list)
                    return list.FirstOrDefault();
                if (value is MediaWithCrops mediaWithCrops)
                    return mediaWithCrops.Content;
                if (value is IEnumerable<MediaWithCrops> mediaList)
                    return mediaList.FirstOrDefault()?.Content;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "PrimaryImagePiece failed to read property {Alias} on content {ContentId}",
                    alias, content.Id);
            }
        }
        return null;
    }
}
