using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Builds a nested Schema.org <see cref="Thing"/> from a <see cref="ComplexTypeConfigModel"/>
/// against an arbitrary content node. Implemented by <c>JsonLdGenerator</c>.
///
/// Resolvers receive this through <see cref="PropertyResolverContext.ComplexTypeBuilder"/> and must
/// NOT take it as a constructor dependency: the builder depends on
/// <see cref="IPropertyValueResolverFactory"/>, which depends on every
/// <see cref="IPropertyValueResolver"/>, so injecting it into a resolver closes a DI cycle that
/// Microsoft.Extensions.DependencyInjection rejects at resolve time. The context already carries
/// <see cref="PropertyResolverContext.ResolverFactory"/> for exactly this reason.
/// </summary>
public interface IComplexTypeBuilder
{
    /// <summary>
    /// Builds the nested Thing, or null when the type is unknown, the config is empty, the node is
    /// already in <paramref name="visitedContentKeys"/>, the depth budget is spent, or nothing
    /// resolved (an empty <c>{"@type":"…"}</c> shell is never emitted).
    /// </summary>
    /// <param name="recursionDepth">Node-hop depth so far. Config nesting does not advance it.</param>
    /// <param name="visitedContentKeys">
    /// Nodes already in the resolution chain. Pass the chain walked so far, NOT
    /// <paramref name="content"/> itself — the builder checks membership before adding.
    /// </param>
    Thing? BuildFromConfig(
        string typeName,
        ComplexTypeConfigModel config,
        IPublishedContent content,
        string? culture,
        int recursionDepth,
        IReadOnlySet<Guid> visitedContentKeys);
}
