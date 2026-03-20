using Microsoft.AspNetCore.Http;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves media picker property values to absolute media URLs for Schema.NET.
/// Handles both single and multi-value media pickers (MediaPicker3 and legacy MediaPicker).
/// Extracts the URL from the media's umbracoFile property to avoid requiring IPublishedUrlProvider.
/// </summary>
public class MediaPickerResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.MediaPicker3", "Umbraco.MediaPicker"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue();
        if (value is null)
            return null;

        // MediaPicker3 returns MediaWithCrops or IEnumerable<MediaWithCrops>
        IPublishedContent? mediaContent = value switch
        {
            MediaWithCrops single => single.Content,
            IEnumerable<MediaWithCrops> multiple => multiple.FirstOrDefault()?.Content,
            IPublishedContent content => content,
            IEnumerable<IPublishedContent> contents => contents.FirstOrDefault(),
            _ => null
        };

        if (mediaContent is null)
            return null;

        var relativeUrl = GetMediaUrl(mediaContent);
        if (string.IsNullOrEmpty(relativeUrl))
            return null;

        return ToAbsoluteUrl(relativeUrl, context.HttpContextAccessor);
    }

    /// <summary>
    /// Extracts the media URL from the umbracoFile property.
    /// The value can be a plain string path, or an ImageCropperValue with a Src property.
    /// </summary>
    private static string? GetMediaUrl(IPublishedContent mediaContent)
    {
        var umbracoFile = mediaContent.GetProperty("umbracoFile");
        if (umbracoFile is null)
            return null;

        var fileValue = umbracoFile.GetValue();
        if (fileValue is null)
            return null;

        // ImageCropperValue has a Src property with the URL
        if (fileValue is ImageCropperValue cropperValue)
            return cropperValue.Src;

        // Plain string path
        return fileValue.ToString();
    }

    private static string ToAbsoluteUrl(string url, IHttpContextAccessor httpContextAccessor)
    {
        if (!url.StartsWith('/'))
            return url;

        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null)
            return url;

        return $"{request.Scheme}://{request.Host}{url}";
    }
}
