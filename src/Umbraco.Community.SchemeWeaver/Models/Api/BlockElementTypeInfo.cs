namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// Information about a block element type available within a BlockList/BlockGrid property.
/// </summary>
public class BlockElementTypeInfo
{
    public string Alias { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Property aliases on this element type. Retained as a plain string array for
    /// backward compatibility with frontends that read <c>properties: string[]</c>.
    /// New callers should prefer <see cref="PropertyInfos"/>.
    /// </summary>
    public List<string> Properties { get; set; } = [];

    /// <summary>
    /// Full per-property info (alias, name, editor alias) for this element type.
    /// Needed by the block suggester and the flat UI to reason about editor types.
    /// </summary>
    public List<BlockElementPropertyInfo> PropertyInfos { get; set; } = [];
}

/// <summary>
/// Full information about a single property on a block element type.
/// </summary>
public class BlockElementPropertyInfo
{
    public string Alias { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EditorAlias { get; set; } = string.Empty;

    /// <summary>
    /// When this property is itself a Block List/Grid (a block nested inside a block), the
    /// element types allowed within it — resolved recursively (depth-capped) so the UI and
    /// the suggester can route nested blocks. Empty for non-block properties.
    /// </summary>
    public List<BlockElementTypeInfo> NestedBlockElementTypes { get; set; } = [];
}
