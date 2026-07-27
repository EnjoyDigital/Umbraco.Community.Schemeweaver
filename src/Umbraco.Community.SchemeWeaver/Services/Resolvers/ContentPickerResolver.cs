using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves content picker property values via the shared <see cref="PickedContentResolver"/>
/// ladder: single-property drill-down (ResolverConfig <c>pickedPropertyAlias</c>), then
/// whole-item nesting (<c>NestedSchemaTypeName</c> + the picked type's own SchemaMapping),
/// then the picked node's name.
/// </summary>
public class ContentPickerResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases => ["Umbraco.ContentPicker"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue(culture: context.Culture);
        if (value is not IPublishedContent pickedContent)
            return null;

        var config = PickedContentResolver.ParseConfig(context.Mapping.ResolverConfig);
        return PickedContentResolver.ResolveItem(pickedContent, context, config);
    }
}
