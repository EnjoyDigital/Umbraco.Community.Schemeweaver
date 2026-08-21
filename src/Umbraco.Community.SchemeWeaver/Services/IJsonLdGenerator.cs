using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Models.Api;

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

    /// <summary>
    /// Returns the base URL (scheme + host) the generator resolves <c>@id</c>
    /// and <c>url</c> tokens against: <c>SchemeWeaver:PublicSiteUrl</c> when
    /// configured, else the current request's host, or null when neither is
    /// available. With no override, in the backoffice this is the backoffice
    /// host, so a preview's resolved URLs can differ from the live render —
    /// callers surface it so editors can see that divergence. Resolves the
    /// same way regardless of the graph/non-graph mode.
    /// </summary>
    string? GetResolvedBaseUrl();

    /// <summary>
    /// Finds a nested block element on <paramref name="page"/> by its <see cref="IPublishedElement.Key"/>,
    /// searching every BlockList/BlockGrid property recursively (top-level blocks, Block Grid areas,
    /// and blocks nested inside a block's own block properties). Returns null when not found.
    /// </summary>
    IPublishedElement? FindBlockInstance(IPublishedContent page, Guid blockInstanceKey, string? culture = null);

    /// <summary>
    /// Renders the REAL JSON-LD a single nested block instance contributes to its page, located by
    /// <paramref name="blockInstanceKey"/> and rendered through the parent page mapping's route for
    /// that block type (so wrapping/transforms/nesting match the in-page emission). The result's
    /// <see cref="BlockInstancePreviewResult.Status"/> distinguishes rendered / not-found / no-route /
    /// empty-after-render.
    /// </summary>
    BlockInstancePreviewResult GenerateBlockInstanceJsonLd(IPublishedContent page, Guid blockInstanceKey, string? culture = null);
}
