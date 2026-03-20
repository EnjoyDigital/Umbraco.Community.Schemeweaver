namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves a property value from published content into a value suitable for Schema.NET.
/// Implementations are selected based on the Umbraco property editor alias.
/// Third-party packages can register additional resolvers via DI.
/// </summary>
public interface IPropertyValueResolver
{
    /// <summary>
    /// The Umbraco property editor aliases this resolver handles.
    /// Return empty to act as a fallback resolver.
    /// </summary>
    IEnumerable<string> SupportedEditorAliases { get; }

    /// <summary>
    /// Priority for resolver selection when multiple resolvers match the same editor alias.
    /// Higher values take precedence. Default is 0.
    /// </summary>
    int Priority => 0;

    /// <summary>
    /// Resolves the property value to a type suitable for Schema.NET assignment.
    /// </summary>
    /// <param name="context">Resolution context containing the content node, property mapping, and services.</param>
    /// <returns>
    /// A resolved value: string for simple values, Thing for nested schema objects,
    /// IEnumerable&lt;Thing&gt; for arrays of nested objects, Uri for URLs, or null if unresolvable.
    /// </returns>
    object? Resolve(PropertyResolverContext context);
}
