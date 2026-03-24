namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves boolean property values for Schema.NET.
/// Preserves the boolean type so Schema.NET serialises them as JSON booleans (true/false).
/// </summary>
public class BooleanResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.TrueFalse"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue();
        if (value is null)
            return null;

        return value switch
        {
            bool b => b,
            int i => i != 0,
            _ => value.ToString()
        };
    }
}
