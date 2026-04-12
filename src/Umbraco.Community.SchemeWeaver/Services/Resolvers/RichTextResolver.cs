using Umbraco.Cms.Core.Strings;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves rich text and markdown property values to HTML strings for Schema.NET.
/// Handles IHtmlEncodedString (from RichText/TinyMCE) and plain string (from Markdown).
/// Further transforms such as stripHtml are applied by <see cref="JsonLdGenerator"/>.
/// </summary>
public class RichTextResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.RichText", "Umbraco.TinyMCE", "Umbraco.MarkdownEditor"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue(culture: context.Culture);
        if (value is null)
            return null;

        return value switch
        {
            IHtmlEncodedString htmlEncoded => htmlEncoded.ToHtmlString(),
            _ => value.ToString()
        };
    }
}
