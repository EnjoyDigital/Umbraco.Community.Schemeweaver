using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Graph;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Generates JSON-LD output from published content using schema mappings.
/// </summary>
public interface IJsonLdGenerator
{
    /// <summary>
    /// Builds a Schema.NET <see cref="Schema.NET.Thing"/> for the content's
    /// mapped schema type. When <paramref name="graphContext"/> is non-null,
    /// property mappings with source type <c>reference</c> resolve their @id
    /// from <see cref="GraphPieceContext.Ids"/> so the emitted Thing carries
    /// cross-piece references; otherwise <c>reference</c> mappings are
    /// silently skipped.
    /// </summary>
    Schema.NET.Thing? GenerateJsonLd(
        IPublishedContent content,
        string? culture = null,
        GraphPieceContext? graphContext = null);
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
