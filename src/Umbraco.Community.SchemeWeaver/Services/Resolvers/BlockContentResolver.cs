using System.Reflection;
using System.Text.Json;
using Schema.NET;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Resolves Block List and Block Grid property values to collections of Schema.NET Things.
/// Reads NestedSchemaTypeName from the mapping to determine the schema type for each block,
/// and ResolverConfig JSON for nested property mappings.
/// </summary>
public class BlockContentResolver : IPropertyValueResolver
{
    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.BlockList", "Umbraco.BlockGrid"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue();
        if (value is null)
            return null;

        // Extract block items from the model
        var blockItems = ExtractBlockItems(value);
        if (blockItems is null || !blockItems.Any())
            return null;

        var nestedSchemaTypeName = context.Mapping.NestedSchemaTypeName;
        if (string.IsNullOrEmpty(nestedSchemaTypeName))
            return null;

        if (context.RecursionDepth >= context.MaxRecursionDepth)
            return null;

        var resolverConfig = ParseResolverConfig(context.Mapping.ResolverConfig);
        var things = new List<Thing>();

        foreach (var blockContent in blockItems)
        {
            var thing = MapBlockToThing(blockContent, nestedSchemaTypeName, resolverConfig, context);
            if (thing is not null)
                things.Add(thing);
        }

        return things.Count > 0 ? things : null;
    }

    private static IEnumerable<IPublishedElement>? ExtractBlockItems(object value)
    {
        return value switch
        {
            BlockListModel blockList => blockList.Select(b => b.Content),
            BlockGridModel blockGrid => blockGrid.Select(b => b.Content),
            _ => null
        };
    }

    private Thing? MapBlockToThing(
        IPublishedElement blockContent,
        string schemaTypeName,
        ResolverConfigModel? config,
        PropertyResolverContext context)
    {
        var clrType = context.SchemaTypeRegistry.GetClrType(schemaTypeName);
        if (clrType is null)
            return null;

        if (Activator.CreateInstance(clrType) is not Thing instance)
            return null;

        var blockAlias = blockContent.ContentType.Alias;

        // Get nested mappings from config for this block type, or fall back to auto-map by name
        var nestedMappings = config?.NestedMappings?
            .Where(m => string.IsNullOrEmpty(m.BlockAlias) ||
                        string.Equals(m.BlockAlias, blockAlias, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (nestedMappings is { Count: > 0 })
        {
            foreach (var mapping in nestedMappings)
            {
                MapPropertyFromConfig(instance, blockContent, mapping, context);
            }
        }
        else
        {
            // Auto-map by matching property names
            AutoMapBlockProperties(instance, blockContent, schemaTypeName, context);
        }

        return instance;
    }

    private static void MapPropertyFromConfig(
        Thing instance,
        IPublishedElement blockContent,
        NestedPropertyMapping mapping,
        PropertyResolverContext context)
    {
        if (string.IsNullOrEmpty(mapping.SchemaProperty) || string.IsNullOrEmpty(mapping.ContentProperty))
            return;

        var prop = blockContent.GetProperty(mapping.ContentProperty);
        var rawValue = prop?.GetValue()?.ToString();
        if (rawValue is null)
            return;

        // Check if we need to wrap the value in another Thing type
        if (!string.IsNullOrEmpty(mapping.WrapInType))
        {
            var wrapType = context.SchemaTypeRegistry.GetClrType(mapping.WrapInType);
            if (wrapType is not null && Activator.CreateInstance(wrapType) is Thing wrapInstance)
            {
                // Set the "text" property on the wrapper (e.g., Answer.Text)
                var textProp = !string.IsNullOrEmpty(mapping.WrapInProperty) ? mapping.WrapInProperty : "Text";
                SetSimplePropertyValue(wrapInstance, textProp, rawValue);
                SetThingPropertyValue(instance, mapping.SchemaProperty, wrapInstance);
                return;
            }
        }

        SetSimplePropertyValue(instance, mapping.SchemaProperty, rawValue);
    }

    private static void AutoMapBlockProperties(
        Thing instance,
        IPublishedElement blockContent,
        string schemaTypeName,
        PropertyResolverContext context)
    {
        var schemaProperties = context.SchemaTypeRegistry.GetProperties(schemaTypeName).ToList();

        foreach (var schemaProp in schemaProperties)
        {
            // Try exact name match (case-insensitive) between block property and schema property
            var prop = blockContent.GetProperty(schemaProp.Name);
            if (prop is null)
                continue;

            var rawValue = prop.GetValue()?.ToString();
            if (rawValue is null)
                continue;

            SetSimplePropertyValue(instance, schemaProp.Name, rawValue);
        }
    }

    private static void SetSimplePropertyValue(Thing instance, string propertyName, string value)
    {
        var property = instance.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is not { CanWrite: true })
            return;

        var targetType = property.PropertyType;

        // Try implicit conversion from string
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

    private static void SetThingPropertyValue(Thing instance, string propertyName, Thing value)
    {
        var property = instance.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is not { CanWrite: true })
            return;

        var targetType = property.PropertyType;

        // Try implicit conversion from Thing
        var methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Implicit" && m.GetParameters().Length == 1);

        foreach (var method in methods)
        {
            var paramType = method.GetParameters()[0].ParameterType;
            if (!paramType.IsAssignableFrom(value.GetType()))
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

    private static ResolverConfigModel? ParseResolverConfig(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ResolverConfigModel>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Model for the ResolverConfig JSON stored on a PropertyMapping.
/// </summary>
public class ResolverConfigModel
{
    public List<NestedPropertyMapping>? NestedMappings { get; set; }
}

/// <summary>
/// Maps a content property on a block to a Schema.org property.
/// </summary>
public class NestedPropertyMapping
{
    /// <summary>
    /// The block element type alias to match. Empty matches all block types.
    /// </summary>
    public string? BlockAlias { get; set; }

    /// <summary>
    /// The Schema.org property name on the nested Thing.
    /// </summary>
    public string? SchemaProperty { get; set; }

    /// <summary>
    /// The content property alias on the block element.
    /// </summary>
    public string? ContentProperty { get; set; }

    /// <summary>
    /// Optional: wrap the value in another Schema.NET type (e.g., "Answer" for FAQ).
    /// </summary>
    public string? WrapInType { get; set; }

    /// <summary>
    /// Optional: the property on the wrap type to set the value on (defaults to "Text").
    /// </summary>
    public string? WrapInProperty { get; set; }
}

/// <summary>
/// Configuration model for complex type property mappings stored in ResolverConfig JSON.
/// </summary>
public class ComplexTypeConfigModel
{
    public List<ComplexTypeMappingEntry>? ComplexTypeMappings { get; set; }
}

/// <summary>
/// Maps a sub-property of a complex Schema.org type to a content property or static value.
/// </summary>
public class ComplexTypeMappingEntry
{
    public string SchemaProperty { get; set; } = string.Empty;
    public string SourceType { get; set; } = "property";   // "property" or "static"
    public string? ContentTypePropertyAlias { get; set; }
    public string? StaticValue { get; set; }
}
