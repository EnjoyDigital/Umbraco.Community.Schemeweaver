namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves dropdown list property values to a string for Schema.NET.
/// The Umbraco.DropDown.Flexible editor always returns IEnumerable&lt;string&gt;,
/// even for single-select mode. This resolver returns the first selected value.
/// </summary>
public class DropdownListResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.DropDown.Flexible"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue(culture: context.Culture);
        if (value is null)
            return null;

        if (value is IEnumerable<string> items)
        {
            var list = items.ToList();
            return list.Count switch
            {
                0 => null,
                1 => list[0],
                _ => string.Join(", ", list)
            };
        }

        return value.ToString();
    }
}
