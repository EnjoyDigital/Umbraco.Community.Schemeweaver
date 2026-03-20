namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Factory that selects the appropriate property value resolver based on the Umbraco property editor alias.
/// </summary>
public interface IPropertyValueResolverFactory
{
    /// <summary>
    /// Gets the resolver for the given property editor alias.
    /// Falls back to the default resolver if no specific resolver is registered.
    /// </summary>
    IPropertyValueResolver GetResolver(string? editorAlias);
}
