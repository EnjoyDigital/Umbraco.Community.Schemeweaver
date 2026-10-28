using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>What a per-content override asks the generator to do with the content type's mapping.</summary>
public enum MappingOverrideKind
{
    /// <summary>Use the content type's mapping as is.</summary>
    None = 0,

    /// <summary>Emit no main entity for this content (site-level pieces are unaffected).</summary>
    Excluded = 1,

    /// <summary>Use <see cref="MappingOverrideResult.Mapping"/>, a clone with a different Schema.org type.</summary>
    Retyped = 2,
}

/// <summary>Result of <see cref="IContentMappingOverrideProvider.Resolve"/>.</summary>
public readonly record struct MappingOverrideResult(MappingOverrideKind Kind, SchemaMapping? Mapping)
{
    public static readonly MappingOverrideResult None = new(MappingOverrideKind.None, null);

    public static readonly MappingOverrideResult Excluded = new(MappingOverrideKind.Excluded, null);

    public static MappingOverrideResult Retyped(SchemaMapping clone) => new(MappingOverrideKind.Retyped, clone);
}

/// <summary>
/// Per-content hook consulted by <see cref="JsonLdGenerator"/> immediately after the content
/// type's mapping has been loaded and found enabled. Lets a satellite exclude one page from
/// structured data, or swap the emitted Schema.org type for a subtype (Article → NewsArticle)
/// while keeping the type's property mappings. This is how the SchemeWeaver SEO satellite's
/// per-page "Schema" tab works.
///
/// Implementations run on the hot path (every node, culture and block render) and must serve
/// from memory. When retyping, return a <b>clone</b> (<see cref="SchemaMapping.Clone"/>) and
/// never mutate the mapping passed in: it is the repository's shared cached snapshot.
/// </summary>
public interface IContentMappingOverrideProvider
{
    MappingOverrideResult Resolve(IPublishedContent content, SchemaMapping mapping, string? culture);
}

/// <summary>Default registration: no overrides. Core behaviour is unchanged.</summary>
public sealed class NullContentMappingOverrideProvider : IContentMappingOverrideProvider
{
    public static readonly NullContentMappingOverrideProvider Instance = new();

    public MappingOverrideResult Resolve(IPublishedContent content, SchemaMapping mapping, string? culture)
        => MappingOverrideResult.None;
}
