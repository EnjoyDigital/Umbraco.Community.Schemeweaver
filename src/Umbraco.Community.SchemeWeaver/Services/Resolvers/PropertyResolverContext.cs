using Microsoft.AspNetCore.Http;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Context passed to property value resolvers during JSON-LD generation.
/// </summary>
public class PropertyResolverContext
{
    /// <summary>
    /// The published content node the property belongs to.
    /// </summary>
    public required IPublishedContent Content { get; init; }

    /// <summary>
    /// The property mapping configuration from the database.
    /// </summary>
    public required PropertyMapping Mapping { get; init; }

    /// <summary>
    /// The alias of the property to resolve on the content node.
    /// </summary>
    public required string PropertyAlias { get; init; }

    /// <summary>
    /// The Schema.NET type registry for looking up nested schema types.
    /// </summary>
    public required ISchemaTypeRegistry SchemaTypeRegistry { get; init; }

    /// <summary>
    /// The mapping repository for looking up nested schema mappings.
    /// </summary>
    public required ISchemaMappingRepository MappingRepository { get; init; }

    /// <summary>
    /// HTTP context accessor for resolving absolute URLs.
    /// </summary>
    public required IHttpContextAccessor HttpContextAccessor { get; init; }

    /// <summary>
    /// The property value resolver factory for resolving nested/block element properties
    /// using the full resolver pipeline (media pickers, dates, etc.).
    /// </summary>
    public IPropertyValueResolverFactory? ResolverFactory { get; init; }

    /// <summary>
    /// The raw published property, already located on the correct node. May be null.
    /// </summary>
    public IPublishedProperty? Property { get; init; }

    /// <summary>
    /// Current recursion depth for nested resolution. Prevents infinite loops.
    /// Maximum depth is 3 by default.
    /// </summary>
    public int RecursionDepth { get; init; }

    /// <summary>
    /// Maximum allowed recursion depth. Default is 3.
    /// </summary>
    public int MaxRecursionDepth { get; init; } = 3;

    /// <summary>
    /// The culture to use when resolving property values. Null for invariant content.
    /// </summary>
    public string? Culture { get; init; }

    /// <summary>
    /// Content keys already in the current resolution chain.
    /// Used to detect circular references (e.g., content A picks content B which picks content A).
    /// </summary>
    public HashSet<Guid> VisitedContentKeys { get; init; } = [];
}
