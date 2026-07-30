using Umbraco.Cms.Core.Strings;
using Umbraco.Community.SchemeWeaver.Services.Transforms;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves rich text and markdown property values to HTML strings for Schema.NET.
/// Handles IHtmlEncodedString (from RichText/TinyMCE) and plain string (from Markdown).
/// Further transforms such as stripHtml are applied by <see cref="JsonLdGenerator"/>.
/// </summary>
public class RichTextResolver : IPropertyValueResolver
{
    // Single source of truth shared with the mapping advisor (which suggests stripHtml when one
    // of these HTML-producing editors feeds a plain-text Schema.org property).
    public IEnumerable<string> SupportedEditorAliases =>
        SchemeWeaverConstants.PropertyEditors.HtmlProducingEditorAliases;

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue(culture: context.Culture);

        if (value == null)
            return null;

        if (value is IHtmlEncodedString text)
        {
            return SchemaValueTransformer.StripHtmlTags(text.ToHtmlString()!) ?? string.Empty;
        }
        return value.ToString();
    }
}
