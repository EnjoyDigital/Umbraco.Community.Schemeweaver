using Umbraco.Cms.Core.Models;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves multi URL picker property values to absolute URL string(s) for Schema.NET.
/// The Umbraco.MultiUrlPicker editor returns IEnumerable&lt;Link&gt;.
/// For single links, returns a string; for multiple, returns the first URL.
/// </summary>
public class MultiUrlPickerResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.MultiUrlPicker"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue();
        if (value is null)
            return null;

        if (value is IEnumerable<Link> links)
        {
            var firstLink = links.FirstOrDefault();
            if (firstLink is null)
                return null;

            var url = firstLink.Url;
            if (string.IsNullOrEmpty(url))
                return null;

            return ToAbsoluteUrl(url, context);
        }

        if (value is Link singleLink)
        {
            var url = singleLink.Url;
            if (string.IsNullOrEmpty(url))
                return null;

            return ToAbsoluteUrl(url, context);
        }

        return value.ToString();
    }

    private static string ToAbsoluteUrl(string url, PropertyResolverContext context)
    {
        if (!url.StartsWith('/'))
            return url;

        var request = context.HttpContextAccessor.HttpContext?.Request;
        if (request is null)
            return url;

        return $"{request.Scheme}://{request.Host}{url}";
    }
}
