using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Generates JSON-LD output from published content using schema mappings.
/// </summary>
public interface IJsonLdGenerator
{
    Schema.NET.Thing? GenerateJsonLd(IPublishedContent content);
    string? GenerateJsonLdString(IPublishedContent content);
}
