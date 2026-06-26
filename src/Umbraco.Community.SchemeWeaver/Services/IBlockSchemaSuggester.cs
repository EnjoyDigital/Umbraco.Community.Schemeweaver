using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Suggests a set of routed block mappings for a BlockList/BlockGrid property: each
/// block element type is matched to a best-fit Schema.org type and a target page
/// property, with per-property auto-mapping against the element type's own properties.
/// </summary>
public interface IBlockSchemaSuggester
{
    /// <summary>
    /// Builds routed mapping suggestions for the supplied block element types.
    /// Returns one <see cref="BlockMappingSuggestion"/> per target page schema property,
    /// each carrying the routes (block element types) that feed it.
    /// </summary>
    IEnumerable<BlockMappingSuggestion> Suggest(IEnumerable<BlockElementTypeInfo> elementTypes);
}
