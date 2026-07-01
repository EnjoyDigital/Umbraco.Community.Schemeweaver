using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.Services.Media;

/// <summary>
/// Builds a Schema.NET <see cref="ImageObject"/> from an Umbraco media node.
/// Centralises the media → ImageObject mapping (absolute URL, intrinsic
/// width/height, best-effort caption) shared by the primary-image graph piece
/// and the media picker property resolver.
/// </summary>
public static class MediaImageObjectFactory
{
    private static readonly string[] _captionAliases =
    [
        "altText",
        "alternativeText",
        "caption",
        "imageAltText"
    ];

    /// <summary>
    /// Creates an <see cref="ImageObject"/> for the supplied media node, or
    /// <c>null</c> when the media yields no resolvable absolute URL (e.g. the
    /// media item has been deleted or its file is missing).
    /// </summary>
    public static ImageObject? Create(IPublishedContent media, IPublishedUrlProvider urlProvider)
    {
        var url = urlProvider.GetMediaUrl(media, UrlMode.Absolute);
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var image = new ImageObject
        {
            Url = uri
        };

        if (media.Value<int?>("umbracoWidth") is int width && width > 0)
            image.Width = new QuantitativeValue { Value = width };
        if (media.Value<int?>("umbracoHeight") is int height && height > 0)
            image.Height = new QuantitativeValue { Value = height };

        var caption = ResolveCaption(media);
        if (!string.IsNullOrWhiteSpace(caption))
        {
            image.Name = caption;
            image.Caption = caption;
        }

        return image;
    }

    private static string? ResolveCaption(IPublishedContent media)
    {
        foreach (var alias in _captionAliases)
        {
            var value = media.Value<string?>(alias);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }
}
