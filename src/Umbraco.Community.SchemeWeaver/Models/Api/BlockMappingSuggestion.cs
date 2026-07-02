using System.Text.Json.Serialization;

namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// A suggested routed block mapping for a single TARGET page schema property
/// (e.g. "mainEntity", "hasPart", "about"). Each carries the block-element routes
/// that feed that target. The frontend turns one of these into a single
/// <see cref="PropertyMappingDto"/> with SourceType "blockContent",
/// SchemaPropertyName = <see cref="SchemaProperty"/>, ContentTypePropertyAlias = the
/// block list property, and ResolverConfig = { "routes": [ ... ] }.
/// </summary>
public class BlockMappingSuggestion
{
    /// <summary>The target page Schema.org property this mapping sets.</summary>
    public string SchemaProperty { get; set; } = string.Empty;

    /// <summary>Aggregate confidence (0–100) across the routes feeding this target.</summary>
    public int Confidence { get; set; }

    /// <summary>The block-element routes that contribute Things to this target property.</summary>
    public List<BlockRouteSuggestion> Routes { get; set; } = [];
}

/// <summary>
/// A suggested route for one block element type: which Schema.org type to instantiate
/// and the per-property mappings to apply. Mirrors the stored ResolverConfig route shape.
/// </summary>
public class BlockRouteSuggestion
{
    public string BlockAlias { get; set; } = string.Empty;
    public string NestedSchemaType { get; set; } = string.Empty;
    public int Confidence { get; set; }

    /// <summary>
    /// Row-scoped fit (additive): when the block-suggest request carried a
    /// <c>targetSchemaProperty</c>, whether <see cref="NestedSchemaType"/> is assignable to
    /// that property's accepted Schema.org range (the same subtype walk
    /// <c>SchemaRangeValidator</c> uses). Routes that don't fit are hints for OTHER targets,
    /// not candidates for the requested row. Null — and omitted from the JSON — when no
    /// target was supplied or its range could not be resolved, so pre-existing clients see
    /// an unchanged payload shape. Only set on top-level routes: nested routes (blocks
    /// inside blocks) feed properties of their parent block's type, not the caller's row.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FitsTarget { get; set; }

    public List<BlockRoutePropertyMappingSuggestion> PropertyMappings { get; set; } = [];
}

/// <summary>
/// A single property mapping within a route: block content property → nested schema property.
/// </summary>
public class BlockRoutePropertyMappingSuggestion
{
    public string SchemaProperty { get; set; } = string.Empty;
    public string ContentProperty { get; set; } = string.Empty;
    public string? WrapInType { get; set; }
    public string? WrapInProperty { get; set; }

    /// <summary>
    /// §3b pre-fill: <c>stripHtml</c> when the source block property is a rich-text editor feeding a
    /// plain-text nested Schema.org property, so the suggested route emits clean text by default.
    /// Serialised onto the stored route as the nested mapping's <c>transformType</c> (the author can
    /// still revert it). Null when no transform is suggested.
    /// </summary>
    public string? TransformType { get; set; }

    /// <summary>
    /// When <see cref="ContentProperty"/> is itself a Block List/Grid (a block nested inside a
    /// block), the suggested routes for that nested block's element types. Serialised onto the
    /// stored mapping as the nested property mapping's own <c>routes</c>, recursing the routing
    /// model one level deeper. Null/empty for ordinary scalar properties.
    /// </summary>
    public List<BlockRouteSuggestion>? Routes { get; set; }
}
