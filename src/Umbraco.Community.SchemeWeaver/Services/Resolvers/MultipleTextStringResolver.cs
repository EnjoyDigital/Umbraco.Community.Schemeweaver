namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves multiple text string property values to a string array for Schema.NET.
/// The Umbraco.MultipleTextstring editor returns IEnumerable&lt;string&gt;.
/// </summary>
public class MultipleTextStringResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.MultipleTextstring"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue(culture: context.Culture);
        if (value is null)
            return null;

        if (value is IEnumerable<string> strings)
        {
            var list = strings.ToList();
            return list.Count > 0 ? list : null;
        }

        return value.ToString();
    }
}
