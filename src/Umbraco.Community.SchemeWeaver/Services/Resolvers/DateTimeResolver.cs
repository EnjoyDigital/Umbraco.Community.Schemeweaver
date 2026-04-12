namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves date/time property values to ISO 8601 strings for Schema.NET.
/// Handles DateTime objects from the Umbraco.DateTime property editor.
/// </summary>
public class DateTimeResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.DateTime"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue(culture: context.Culture);
        if (value is null)
            return null;

        return value switch
        {
            DateTime dt => dt.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }
}
