namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves numeric property values for Schema.NET.
/// Preserves the numeric type (int/decimal) rather than converting to string,
/// so Schema.NET serialises them as JSON numbers.
/// </summary>
public class NumericResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.Integer", "Umbraco.Decimal"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue();
        if (value is null)
            return null;

        // Return the numeric value directly so Schema.NET serialises as a JSON number
        return value switch
        {
            int or long or decimal or double or float => value,
            _ => value.ToString()
        };
    }
}
