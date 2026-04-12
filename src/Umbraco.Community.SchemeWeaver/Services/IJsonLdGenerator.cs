using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Generates JSON-LD output from published content using schema mappings.
/// </summary>
public interface IJsonLdGenerator
{
    Schema.NET.Thing? GenerateJsonLd(IPublishedContent content, string? culture = null);
    string? GenerateJsonLdString(IPublishedContent content, string? culture = null);

    /// <summary>
    /// Generates a BreadcrumbList JSON-LD string from the content's ancestor hierarchy.
    /// Returns null for root content (no meaningful breadcrumb trail).
    /// </summary>
    string? GenerateBreadcrumbJsonLd(IPublishedContent content, string? culture = null);

    /// <summary>
    /// Generates JSON-LD strings from inherited schema mappings on ancestor content nodes.
    /// Walks up the parent chain and for each ancestor whose content type has a mapping
    /// with IsInherited = true, generates the JSON-LD from that ancestor's content.
    /// </summary>
    IEnumerable<string> GenerateInheritedJsonLdStrings(IPublishedContent content, string? culture = null);

    /// <summary>
    /// Scans all BlockList/BlockGrid properties on the content and generates JSON-LD
    /// for any block elements whose content types have their own schema mappings.
    /// Properties already explicitly mapped via blockContent source type are skipped.
    /// </summary>
    IEnumerable<string> GenerateBlockElementJsonLdStrings(IPublishedContent content, string? culture = null);
}
