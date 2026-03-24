namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves tag property values to a comma-separated string for Schema.NET.
/// The Umbraco.Tags editor returns IEnumerable&lt;string&gt;.
/// </summary>
public class TagsResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.Tags"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue();
        if (value is null)
            return null;

        if (value is IEnumerable<string> tags)
        {
            var tagList = tags.ToList();
            return tagList.Count > 0 ? string.Join(", ", tagList) : null;
        }

        return value.ToString();
    }
}
