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
    /// <param name="elementTypes">The block element types configured on the Block List/Grid property.</param>
    /// <param name="pageSchemaType">
    /// The page's mapped Schema.org type (from the saved mapping), when known. Gates
    /// type-specific targets — a testimonial/review block targets <c>review</c> only when
    /// this type declares that property (Product does; a page type without it falls back
    /// to <c>hasPart</c>) — and resolves <paramref name="targetSchemaProperty"/>'s range.
    /// Null preserves the context-free legacy behaviour.
    /// </param>
    /// <param name="targetSchemaProperty">
    /// Optional row scope: the parent property-mapping row's schema property. When present
    /// (and resolvable against <paramref name="pageSchemaType"/>), every top-level
    /// <see cref="BlockRouteSuggestion"/> gets <see cref="BlockRouteSuggestion.FitsTarget"/>
    /// set to whether its nested type fits that property's accepted range; otherwise
    /// <c>FitsTarget</c> stays null.
    /// </param>
    IEnumerable<BlockMappingSuggestion> Suggest(
        IEnumerable<BlockElementTypeInfo> elementTypes,
        string? pageSchemaType = null,
        string? targetSchemaProperty = null);
}
