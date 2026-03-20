using System.Reflection;
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
        var value = context.Property?.GetValue();
        if (value is not IPublishedContent pickedContent)
            return null;

        // If a nested schema type is configured and recursion allows, generate a nested Thing
        if (!string.IsNullOrEmpty(context.Mapping.NestedSchemaTypeName)
            && context.RecursionDepth < context.MaxRecursionDepth)
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

        var propertyMappings = context.MappingRepository.GetPropertyMappings(nestedMapping.Id);

        foreach (var propMapping in propertyMappings)
        {
            if (string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias))
                continue;

            var prop = content.GetProperty(propMapping.ContentTypePropertyAlias);
            var propValue = prop?.GetValue()?.ToString();
            if (propValue is null)
                continue;

            SetSimplePropertyValue(instance, propMapping.SchemaPropertyName, propValue);
        }

        return instance;
    }

    private static void SetSimplePropertyValue(Thing instance, string propertyName, string value)
    {
        var property = instance.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is not { CanWrite: true })
            return;

        var targetType = property.PropertyType;

        // Try implicit conversion
        var methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Implicit" && m.GetParameters().Length == 1);

        foreach (var method in methods)
        {
            var paramType = method.GetParameters()[0].ParameterType;
            if (!paramType.IsAssignableFrom(typeof(string)))
                continue;

            try
            {
                var converted = method.Invoke(null, [value]);
                property.SetValue(instance, converted);
                return;
            }
            catch
            {
                // Continue trying other conversions
            }
        }
    }

}
