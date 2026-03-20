namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Default property value resolver that extracts the string representation of a property value.
/// This replicates the original behaviour of calling GetValue()?.ToString().
/// </summary>
public class DefaultPropertyValueResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases => [];

    public int Priority => 0;

    public object? Resolve(PropertyResolverContext context)
    {
        return context.Property?.GetValue()?.ToString();
    }
}
