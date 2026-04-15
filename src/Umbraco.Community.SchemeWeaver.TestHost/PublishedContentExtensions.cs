using Umbraco.Cms.Core.Models.PublishedContent;
namespace Umbraco.Community.SchemeWeaver.TestHost;

public static class PublishedContentExtensions
{
    public static string? GetTypedDescription(this IPublishedContent content)
    {
        return content
            .GetType()
            .GetProperty("Description")
            ?.GetValue(content) as string;
    }
}
