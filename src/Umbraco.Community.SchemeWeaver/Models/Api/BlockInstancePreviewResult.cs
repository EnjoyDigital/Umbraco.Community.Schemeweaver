namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// Outcome of resolving a single nested block instance to JSON-LD for preview.
/// </summary>
public enum BlockInstanceResolutionStatus
{
    /// <summary>The block was found, routed, and rendered to JSON-LD.</summary>
    Rendered,

    /// <summary>No block element with the given key exists on the page.</summary>
    BlockNotFound,

    /// <summary>The block exists but no route on the page's mapping maps its type to a schema type.</summary>
    NoRouteForBlock,

    /// <summary>The block was routed but resolved no usable (or required) properties, so it would not emit.</summary>
    EmptyAfterRender
}

/// <summary>
/// Internal result of <c>IJsonLdGenerator.GenerateBlockInstanceJsonLd</c>: the rendered JSON-LD
/// (when <see cref="Status"/> is <see cref="BlockInstanceResolutionStatus.Rendered"/>) plus the
/// context needed to explain where it resolved from.
/// </summary>
public sealed record BlockInstancePreviewResult(
    BlockInstanceResolutionStatus Status,
    string? JsonLd,
    string? ResolvedFromNodeName,
    Guid ResolvedFromNodeKey,
    string? BlockAlias,
    string? SchemaType);
