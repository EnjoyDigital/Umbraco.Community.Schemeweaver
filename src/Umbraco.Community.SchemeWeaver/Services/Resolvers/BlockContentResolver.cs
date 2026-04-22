using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<BlockContentResolver> _logger;

    public BlockContentResolver(ILogger<BlockContentResolver> logger)
    {
        _logger = logger;
    }

    public IEnumerable<string> SupportedEditorAliases =>
        ["Umbraco.BlockList", "Umbraco.BlockGrid"];

    public int Priority => 10;

    public object? Resolve(PropertyResolverContext context)
    {
        var value = context.Property?.GetValue(culture: context.Culture);
        if (value is null)
            return null;

        // Extract block items from the model
        var blockItems = ExtractBlockItems(value);
        if (blockItems is null || !blockItems.Any())
            return null;

        if (context.RecursionDepth >= context.MaxRecursionDepth)
            return null;

        var resolverConfig = ParseResolverConfig(context.Mapping.ResolverConfig);

        // String extraction mode: return List<string> from block items (e.g., recipeIngredient)
        if (resolverConfig?.ExtractAs == "stringList" && !string.IsNullOrEmpty(resolverConfig.ContentProperty))
        {
            var strings = new List<string>();
            foreach (var blockContent in blockItems)
            {
                var rawValue = SchemaPropertySetter.ResolveElementPropertyValue(
                    blockContent, resolverConfig.ContentProperty, context.HttpContextAccessor);
                if (rawValue is string s && !string.IsNullOrEmpty(s))
                    strings.Add(s);
            }
            return strings.Count > 0 ? strings : null;
        }

        var nestedSchemaTypeName = context.Mapping.NestedSchemaTypeName;
        if (string.IsNullOrEmpty(nestedSchemaTypeName))
        {
            _logger.LogWarning(
                "Block content resolver for property '{PropertyAlias}' on content '{ContentName}' has no NestedSchemaTypeName configured — block items cannot be mapped to Schema.org types",
                context.PropertyAlias, context.Content.Name);
            return null;
        }

        var things = blockItems
            .Select(blockContent => MapBlockToThing(blockContent, nestedSchemaTypeName, resolverConfig, context))
            .Where(thing => thing is not null)
            .Cast<Thing>()
            .ToList();

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
            // Group mappings that target the same (schemaProperty, wrapInType) so multi-
            // sub-property wrappers — e.g. Place.geo via GeoCoordinates (latitude AND
            // longitude) — collapse onto a single wrapper instance. Prior to this the
            // resolver recreated the wrapper on every mapping in the group, so only the
            // last sub-property survived and everything emitted before was silently lost.
            var grouped = nestedMappings.GroupBy(m => (
                SchemaProperty: m.SchemaProperty ?? string.Empty,
                WrapInType: m.WrapInType ?? string.Empty));

            foreach (var group in grouped)
            {
                if (string.IsNullOrEmpty(group.Key.WrapInType))
                {
                    // No wrapper — apply each mapping independently.
                    foreach (var mapping in group)
                        MapPropertyFromConfig(instance, blockContent, mapping, context);
                }
                else
                {
                    ApplyWrappedGroup(
                        instance, blockContent,
                        group.Key.SchemaProperty, group.Key.WrapInType,
                        group, context);
                }
            }
        }
        else
        {
            // Auto-map by matching property names
            AutoMapBlockProperties(instance, blockContent, schemaTypeName, context);
        }

        return instance;
    }

    /// <summary>
    /// Apply every mapping in <paramref name="group"/> to a single wrapper instance
    /// of type <paramref name="wrapInType"/>, then assign that wrapper to
    /// <paramref name="schemaProperty"/> on <paramref name="instance"/> exactly once.
    /// Skips the group entirely if no sub-mapping resolves to a usable value — avoids
    /// emitting an empty wrapper shell (e.g. an empty GeoCoordinates on a Place that
    /// has no map block at all).
    /// </summary>
    private static void ApplyWrappedGroup(
        Thing instance,
        IPublishedElement blockContent,
        string schemaProperty,
        string wrapInType,
        IEnumerable<NestedPropertyMapping> group,
        PropertyResolverContext context)
    {
        if (string.IsNullOrEmpty(schemaProperty))
            return;

        var wrapType = context.SchemaTypeRegistry.GetClrType(wrapInType);
        if (wrapType is null || Activator.CreateInstance(wrapType) is not Thing wrapInstance)
            return;

        var wroteAtLeastOne = false;
        foreach (var mapping in group)
        {
            if (string.IsNullOrEmpty(mapping.ContentProperty))
                continue;

            var rawValue = ResolveBlockElementProperty(blockContent, mapping.ContentProperty, context);
            if (rawValue is null)
                continue;
            if (rawValue is string s && string.IsNullOrWhiteSpace(s))
                continue;

            var wrapPropertyName = !string.IsNullOrEmpty(mapping.WrapInProperty)
                ? mapping.WrapInProperty
                : InferWrapProperty(wrapInType, mapping.ContentProperty, context.SchemaTypeRegistry);

            SchemaPropertySetter.SetPropertyValue(wrapInstance, wrapPropertyName, rawValue);
            wroteAtLeastOne = true;
        }

        if (wroteAtLeastOne)
            SchemaPropertySetter.SetPropertyValue(instance, schemaProperty, wrapInstance);
    }

    private static void MapPropertyFromConfig(
        Thing instance,
        IPublishedElement blockContent,
        NestedPropertyMapping mapping,
        PropertyResolverContext context)
    {
        if (string.IsNullOrEmpty(mapping.SchemaProperty) || string.IsNullOrEmpty(mapping.ContentProperty))
            return;

        // Use the full resolver pipeline when available (handles media pickers, dates, etc.)
        var rawValue = ResolveBlockElementProperty(blockContent, mapping.ContentProperty, context);
        if (rawValue is null)
            return;

        // Guard against empty string values to avoid generating empty wrapper types
        if (rawValue is string s && string.IsNullOrWhiteSpace(s))
            return;

        // Check if we need to wrap the value in another Thing type
        if (!string.IsNullOrEmpty(mapping.WrapInType))
        {
            var wrapType = context.SchemaTypeRegistry.GetClrType(mapping.WrapInType);
            if (wrapType is not null && Activator.CreateInstance(wrapType) is Thing wrapInstance)
            {
                // Determine wrapper property: explicit config, inferred from content property, or "Text" fallback
                var wrapPropertyName = !string.IsNullOrEmpty(mapping.WrapInProperty)
                    ? mapping.WrapInProperty
                    : InferWrapProperty(mapping.WrapInType, mapping.ContentProperty, context.SchemaTypeRegistry);
                SchemaPropertySetter.SetPropertyValue(wrapInstance, wrapPropertyName, rawValue);
                SchemaPropertySetter.SetPropertyValue(instance, mapping.SchemaProperty, wrapInstance);
                return;
            }
        }

        SchemaPropertySetter.SetPropertyValue(instance, mapping.SchemaProperty, rawValue);
    }

    /// <summary>
    /// Resolves a property value from a block element.
    /// Delegates to <see cref="SchemaPropertySetter.ResolveElementPropertyValue"/> which handles
    /// media pickers, editor alias detection, and string extraction.
    /// </summary>
    private static object? ResolveBlockElementProperty(
        IPublishedElement blockContent,
        string propertyAlias,
        PropertyResolverContext context)
    {
        return SchemaPropertySetter.ResolveElementPropertyValue(
            blockContent, propertyAlias, context.HttpContextAccessor);
    }

    /// <summary>
    /// Infers the best property on the wrapper type to set the value on,
    /// by matching the content property name against the wrapper type's schema properties.
    /// Falls back to "Text" if no match found.
    /// </summary>
    private static string InferWrapProperty(string wrapTypeName, string? contentPropertyName, ISchemaTypeRegistry registry)
    {
        if (!string.IsNullOrEmpty(contentPropertyName))
        {
            var wrapProps = registry.GetProperties(wrapTypeName).ToList();

            // Exact match (case-insensitive)
            var exact = wrapProps.FirstOrDefault(p =>
                string.Equals(p.Name, contentPropertyName, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact.Name;

            // Partial match
            var partial = wrapProps.FirstOrDefault(p =>
                p.Name.Contains(contentPropertyName, StringComparison.OrdinalIgnoreCase)
                || contentPropertyName.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
            if (partial is not null)
                return partial.Name;
        }

        return "Text";
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
            var rawValue = SchemaPropertySetter.ResolveElementPropertyValue(
                blockContent, schemaProp.Name, context.HttpContextAccessor);
            if (rawValue is null)
                continue;

            SchemaPropertySetter.SetPropertyValue(instance, schemaProp.Name, rawValue);
        }
    }

    private ResolverConfigModel? ParseResolverConfig(string? json)
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
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse block content ResolverConfig JSON: {Json}", json);
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

    /// <summary>
    /// When set to "stringList", block items are extracted as strings instead of Things.
    /// Used for properties like recipeIngredient that expect string arrays.
    /// </summary>
    public string? ExtractAs { get; set; }

    /// <summary>
    /// The block element property alias to read when using string extraction mode.
    /// </summary>
    public string? ContentProperty { get; set; }
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
    public string? SelectedSubType { get; set; }
    public List<ComplexTypeMappingEntry>? ComplexTypeMappings { get; set; }
}

/// <summary>
/// Maps a sub-property of a complex Schema.org type to a content property or static value.
/// </summary>
public class ComplexTypeMappingEntry
{
    public string SchemaProperty { get; set; } = string.Empty;
    public string SourceType { get; set; } = "property";   // "property", "static", or "complexType"
    public string? ContentTypePropertyAlias { get; set; }
    public string? StaticValue { get; set; }
    public string? SourceContentTypeAlias { get; set; }
    public string? ResolverConfig { get; set; }
}
