using Umbraco.Cms.Core.Models;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves multi URL picker property values to absolute URL string(s) for Schema.NET.
/// The Umbraco.MultiUrlPicker editor returns IEnumerable&lt;Link&gt;.
/// Returns a plain string for a single link and a <c>List&lt;string&gt;</c> of ALL URLs for
/// several — dropping every link after the first would silently lose data the editor
/// deliberately entered (e.g. social profiles feeding Organization.sameAs).
/// </summary>
public class MultiUrlPickerResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.MultiUrlPicker"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue(culture: context.Culture);
        if (value is null)
            return null;

        if (value is IEnumerable<Link> links)
        {
            var urls = links
                .Select(link => link.Url)
                .Where(url => !string.IsNullOrEmpty(url))
                .Select(url => ToAbsoluteUrl(url!, context))
                .ToList();

            return urls.Count switch
            {
                0 => null,
                1 => urls[0],
                _ => urls
            };
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
