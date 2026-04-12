using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves content picker property values to URLs, names, or nested Schema.NET Things.
/// When a NestedSchemaTypeName is configured and recursion depth permits,
/// generates a nested Thing from the picked content's schema mapping.
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

        // If a nested schema type is configured, recursion depth permits, and we haven't
        // already visited this content node (circular reference guard), generate a nested Thing
        if (!string.IsNullOrEmpty(context.Mapping.NestedSchemaTypeName)
            && context.RecursionDepth < context.MaxRecursionDepth
            && !context.VisitedContentKeys.Contains(pickedContent.Key))
        {
            var nestedThing = GenerateNestedThing(pickedContent, context);
            if (nestedThing is not null)
                return nestedThing;
        }

        // Fallback: return the content name (URL resolution requires IPublishedUrlProvider
        // which is not available in all contexts; content URL is better handled via transforms)
        return pickedContent.Name;
    }

    private static Thing? GenerateNestedThing(IPublishedContent content, PropertyResolverContext context)
    {
        var clrType = context.SchemaTypeRegistry.GetClrType(context.Mapping.NestedSchemaTypeName!);
        if (clrType is null)
            return null;

        if (Activator.CreateInstance(clrType) is not Thing instance)
            return null;

        // Look up the mapping for the picked content's type
        var nestedMapping = context.MappingRepository.GetByContentTypeAlias(content.ContentType.Alias);
        if (nestedMapping is null)
            return null;

        // Build the visited set for child resolution: current chain + the content node we're resolving from
        var childVisitedKeys = new HashSet<Guid>(context.VisitedContentKeys) { context.Content.Key };

        var propertyMappings = context.MappingRepository.GetPropertyMappings(nestedMapping.Id);

        foreach (var propMapping in propertyMappings)
        {
            if (string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias))
                continue;

            // Use the resolver pipeline when available so nested content pickers,
            // media pickers, etc. are resolved correctly with depth/cycle tracking
            if (context.ResolverFactory is not null)
            {
                var publishedProperty = content.GetProperty(propMapping.ContentTypePropertyAlias);
                var editorAlias = publishedProperty?.PropertyType?.EditorAlias;
                var resolver = context.ResolverFactory.GetResolver(editorAlias);

                var childContext = new PropertyResolverContext
                {
                    Content = content,
                    Mapping = propMapping,
                    PropertyAlias = propMapping.ContentTypePropertyAlias,
                    SchemaTypeRegistry = context.SchemaTypeRegistry,
                    MappingRepository = context.MappingRepository,
                    HttpContextAccessor = context.HttpContextAccessor,
                    ResolverFactory = context.ResolverFactory,
                    Property = publishedProperty,
                    RecursionDepth = context.RecursionDepth + 1,
                    MaxRecursionDepth = context.MaxRecursionDepth,
                    VisitedContentKeys = childVisitedKeys,
                    Culture = context.Culture
                };

                var resolvedValue = resolver.Resolve(childContext);
                if (resolvedValue is null)
                    continue;

                SchemaPropertySetter.SetPropertyValue(instance, propMapping.SchemaPropertyName, resolvedValue);
            }
            else
            {
                // Fallback: simple value extraction without the resolver pipeline
                var resolvedValue = SchemaPropertySetter.ResolveElementPropertyValue(
                    content, propMapping.ContentTypePropertyAlias, context.HttpContextAccessor);
                if (resolvedValue is null)
                    continue;

                SchemaPropertySetter.SetPropertyValue(instance, propMapping.SchemaPropertyName, resolvedValue);
            }
        }

        return instance;
    }

}
