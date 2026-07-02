using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Community.SchemeWeaver.Services.Media;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves media picker property values to Schema.NET <see cref="ImageObject"/>(s).
/// Handles both single and multi-value media pickers (MediaPicker3 and legacy MediaPicker).
/// Returns a single <see cref="ImageObject"/> for one media item, a
/// <see cref="List{ImageObject}"/> for several, or <c>null</c> when no media resolves.
/// Each ImageObject carries an absolute URL plus intrinsic width/height via
/// <see cref="MediaImageObjectFactory"/>.
/// </summary>
public class MediaPickerResolver : IPropertyValueResolver
{
    private readonly ILogger<MediaPickerResolver> _logger;
    private readonly IPublishedUrlProvider _urlProvider;

    public MediaPickerResolver(
        ILogger<MediaPickerResolver> logger,
        IPublishedUrlProvider urlProvider)
    {
        _logger = logger;
        _urlProvider = urlProvider;
    }

    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.MediaPicker3", "Umbraco.MediaPicker"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue(culture: context.Culture);
        if (value is null)
            return null;

        // MediaPicker3 returns MediaWithCrops or IEnumerable<MediaWithCrops>;
        // legacy MediaPicker returns IPublishedContent or IEnumerable<IPublishedContent>.
        IEnumerable<IPublishedContent?> mediaNodes = value switch
        {
            MediaWithCrops single => [single.Content],
            IEnumerable<MediaWithCrops> multiple => multiple.Select(m => m.Content),
            IPublishedContent content => [content],
            IEnumerable<IPublishedContent> contents => contents,
            _ => []
        };

        var images = new List<ImageObject>();
        foreach (var node in mediaNodes.OfType<IPublishedContent>())
        {
            var image = MediaImageObjectFactory.Create(node, _urlProvider);
            if (image is not null)
                images.Add(image);
        }

        if (images.Count == 0)
        {
            _logger.LogWarning(
                "Media picker property '{PropertyAlias}' on content '{ContentName}' resolved to no usable media — items may have been deleted or their files are missing",
                context.PropertyAlias, context.Content.Name);
            return null;
        }

        return images.Count == 1 ? images[0] : images;
    }
}
