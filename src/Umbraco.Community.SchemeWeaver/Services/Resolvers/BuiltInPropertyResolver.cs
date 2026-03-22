using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves built-in IPublishedContent properties (URL, Name, CreateDate, UpdateDate)
/// that are not accessible via <see cref="IPublishedContent.GetProperty"/>.
/// </summary>
public class BuiltInPropertyResolver : IPropertyValueResolver
{
    private readonly IPublishedUrlProvider _urlProvider;

    public BuiltInPropertyResolver(IPublishedUrlProvider urlProvider)
    {
        _urlProvider = urlProvider;
    }

    public IEnumerable<string> SupportedEditorAliases =>
        [SchemeWeaverConstants.BuiltInProperties.EditorAlias];

    public int Priority => 20;

    public object? Resolve(PropertyResolverContext context)
    {
        var content = context.Content;

        return context.PropertyAlias switch
        {
            SchemeWeaverConstants.BuiltInProperties.Url => ResolveUrl(content, context),
            SchemeWeaverConstants.BuiltInProperties.Name => content.Name,
            SchemeWeaverConstants.BuiltInProperties.CreateDate => content.CreateDate.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            SchemeWeaverConstants.BuiltInProperties.UpdateDate => content.UpdateDate.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            _ => null
        };
    }

    private string? ResolveUrl(IPublishedContent content, PropertyResolverContext context)
    {
        var url = _urlProvider.GetUrl(content, UrlMode.Absolute);
        if (!string.IsNullOrEmpty(url) && url != "#")
            return url;

        // Fallback: build absolute URL from relative + request context
        var relativeUrl = _urlProvider.GetUrl(content, UrlMode.Relative);
        if (string.IsNullOrEmpty(relativeUrl) || relativeUrl == "#")
            return null;

        var request = context.HttpContextAccessor.HttpContext?.Request;
        if (request is null)
            return relativeUrl;

        return $"{request.Scheme}://{request.Host}{relativeUrl}";
    }
}
