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
}
