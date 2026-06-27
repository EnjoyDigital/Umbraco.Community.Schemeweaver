using System.Text.Json;
using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;

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

        // New routed form: each block ELEMENT TYPE has its own route (its own
        // nested schema type + property mappings). A single block list referenced
        // by several property mappings carries, on each mapping, only the routes
        // (block element types) that feed that mapping's own target schema property.
        // Block items whose alias has no route are skipped (debug, not warn-spammed),
        // so a heterogeneous list emits a differently-typed Thing per element type.
        // When routes are present the mapping-level NestedSchemaTypeName is irrelevant
        // and its absence must NOT abort resolution.
        if (resolverConfig?.Routes is { Count: > 0 } routes)
        {
            var routedThings = blockItems
                .Select(blockContent => MapBlockViaRoute(blockContent, routes, context))
                .Where(thing => thing is not null)
                .Cast<Thing>()
                .ToList();

            return routedThings.Count > 0 ? routedThings : null;
        }

        // Legacy single-route form: one NestedSchemaTypeName + a flat NestedMappings
        // list applies to every block in the list. Treated as one implicit route.
        var nestedSchemaTypeName = context.Mapping.NestedSchemaTypeName;
        if (string.IsNullOrEmpty(nestedSchemaTypeName))
        {
            _logger.LogWarning(
                "Block content resolver for property '{PropertyAlias}' on content '{ContentName}' has no NestedSchemaTypeName configured — block items cannot be mapped to Schema.org types",
                context.PropertyAlias, context.Content.Name);
            return null;
        }

        var things = blockItems
            .Select(blockContent => MapBlockToThing(blockContent, nestedSchemaTypeName, resolverConfig?.NestedMappings, context))
            .Where(thing => thing is not null)
            .Cast<Thing>()
            .ToList();

        return things.Count > 0 ? things : null;
    }

    /// <summary>
    /// Resolves the route for a single block item by its content type alias and maps
    /// it to a typed Schema.NET Thing. An exact <see cref="BlockRoute.BlockAlias"/>
    /// match wins; otherwise a wildcard route (empty <see cref="BlockRoute.BlockAlias"/>)
    /// applies to any block. Returns null (logged at debug) when no route matches or
    /// the matched route has no nested schema type — those block items are simply
    /// dropped from this mapping's output rather than aborting the whole list.
    /// </summary>
    private Thing? MapBlockViaRoute(
        IPublishedElement blockContent,
        List<BlockRoute> routes,
        PropertyResolverContext context)
    {
        var blockAlias = blockContent.ContentType.Alias;

        var route = routes.FirstOrDefault(r =>
                        string.Equals(r.BlockAlias, blockAlias, StringComparison.OrdinalIgnoreCase))
                    ?? routes.FirstOrDefault(r => string.IsNullOrEmpty(r.BlockAlias));

        if (route is null || string.IsNullOrEmpty(route.NestedSchemaType))
        {
            _logger.LogDebug(
                "Block '{BlockAlias}' on content '{ContentName}' (property '{PropertyAlias}') has no schema route — skipping",
                blockAlias, context.Content.Name, context.PropertyAlias);
            return null;
        }

        return MapBlockToThing(blockContent, route.NestedSchemaType, route.PropertyMappings, context);
    }

    private static IEnumerable<IPublishedElement>? ExtractBlockItems(object value)
    {
        return value switch
        {
            BlockListModel blockList => blockList.Select(b => b.Content),
            // Block Grid stores nested blocks inside layout Areas. Flatten the whole grid
            // (top-level items + every area, recursively) so blocks placed in an area are
            // not silently dropped. Area layout itself carries no Schema.org meaning, so it
            // is flattened into one ordered sequence.
            BlockGridModel blockGrid => FlattenGridItems(blockGrid),
            _ => null
        };
    }

    /// <summary>
    /// Depth-first flatten of a Block Grid's items, descending through each item's
    /// <see cref="BlockGridItem.Areas"/> so that blocks nested inside grid areas are included.
    /// </summary>
    private static IEnumerable<IPublishedElement> FlattenGridItems(IEnumerable<BlockGridItem> items)
    {
        foreach (var item in items)
        {
            yield return item.Content;

            foreach (var area in item.Areas)
                foreach (var areaItem in FlattenGridItems(area))
                    yield return areaItem;
        }
    }

    private Thing? MapBlockToThing(
        IPublishedElement blockContent,
        string schemaTypeName,
        List<NestedPropertyMapping>? configuredMappings,
        PropertyResolverContext context)
    {
        var clrType = context.SchemaTypeRegistry.GetClrType(schemaTypeName);
        if (clrType is null)
            return null;

        if (Activator.CreateInstance(clrType) is not Thing instance)
            return null;

        var blockAlias = blockContent.ContentType.Alias;

        // Filter the supplied mappings to those applicable to this block type
        // (empty BlockAlias matches all), or fall back to auto-map by name.
        // Route property mappings carry no per-mapping BlockAlias (the route itself
        // is keyed by block alias), so they pass through this filter untouched.
        var nestedMappings = configuredMappings?
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

    private void MapPropertyFromConfig(
        Thing instance,
        IPublishedElement blockContent,
        NestedPropertyMapping mapping,
        PropertyResolverContext context)
    {
        if (string.IsNullOrEmpty(mapping.SchemaProperty) || string.IsNullOrEmpty(mapping.ContentProperty))
            return;

        // Nested block editor: when the mapped block property is itself a Block List/Grid,
        // recurse to produce nested Schema.NET Things (or a string list) rather than the
        // useless ToString() of the block model. Assigned directly to the schema property.
        var nestedProperty = blockContent.GetProperty(mapping.ContentProperty);
        if (IsBlockEditor(nestedProperty?.PropertyType?.EditorAlias))
        {
            var nested = ResolveNestedBlockProperty(blockContent, nestedProperty!, mapping, context);
            if (nested is not null)
                SchemaPropertySetter.SetPropertyValue(instance, mapping.SchemaProperty, nested);
            return;
        }

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

    /// <summary>True when the editor alias is a Block List or Block Grid.</summary>
    internal static bool IsBlockEditor(string? editorAlias) =>
        editorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid";

    /// <summary>
    /// Resolves a block element property that is itself a Block List/Grid (a block nested
    /// inside a block) by re-entering <see cref="Resolve"/> with a child context one level
    /// deeper. The nested route config (<see cref="NestedPropertyMapping.Routes"/> or a
    /// <c>stringList</c> extraction) is serialised onto a synthetic <see cref="PropertyMapping"/>.
    /// Depth- and cycle-guarded so a block referencing its own type cannot loop forever.
    /// </summary>
    private object? ResolveNestedBlockProperty(
        IPublishedElement blockContent,
        IPublishedProperty nestedProperty,
        NestedPropertyMapping mapping,
        PropertyResolverContext context)
    {
        // One level deeper than the current block. Bail before exceeding the cap (Resolve
        // also guards, but checking here avoids building a throwaway child context).
        if (context.RecursionDepth + 1 >= context.MaxRecursionDepth)
        {
            _logger.LogDebug(
                "Nested block property '{PropertyAlias}' on block '{BlockAlias}' skipped — recursion depth {Depth} would exceed max {Max}",
                nestedProperty.Alias, blockContent.ContentType.Alias, context.RecursionDepth + 1, context.MaxRecursionDepth);
            return null;
        }

        if (context.VisitedContentKeys.Contains(blockContent.Key))
            return null;

        var nestedConfig = mapping.ExtractAs == "stringList"
            ? new ResolverConfigModel { ExtractAs = "stringList", ContentProperty = mapping.NestedContentProperty }
            : new ResolverConfigModel { Routes = mapping.Routes };

        var childMapping = new PropertyMapping
        {
            SchemaPropertyName = mapping.SchemaProperty!,
            SourceType = "blockContent",
            ContentTypePropertyAlias = nestedProperty.Alias,
            ResolverConfig = JsonSerializer.Serialize(nestedConfig)
        };

        var childVisited = new HashSet<Guid>(context.VisitedContentKeys) { blockContent.Key };

        var childContext = new PropertyResolverContext
        {
            Content = context.Content,
            Mapping = childMapping,
            PropertyAlias = nestedProperty.Alias,
            SchemaTypeRegistry = context.SchemaTypeRegistry,
            MappingRepository = context.MappingRepository,
            HttpContextAccessor = context.HttpContextAccessor,
            ResolverFactory = context.ResolverFactory,
            Property = nestedProperty,
            RecursionDepth = context.RecursionDepth + 1,
            MaxRecursionDepth = context.MaxRecursionDepth,
            VisitedContentKeys = childVisited,
            Culture = context.Culture
        };

        return Resolve(childContext);
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
            // Skip nested Block List/Grid properties — auto-map has no schema route for their
            // child blocks, and ResolveElementPropertyValue would otherwise stamp the block
            // model's ToString() onto the schema property. Nested blocks require explicit routes.
            if (IsBlockEditor(blockContent.GetProperty(schemaProp.Name)?.PropertyType?.EditorAlias))
                continue;

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
    /// <summary>
    /// New per-block-type routing form. Each route targets one block ELEMENT TYPE
    /// (by <see cref="BlockRoute.BlockAlias"/>) and gives it its own nested Schema.org
    /// type and property mappings. When present this takes precedence over the legacy
    /// <see cref="NestedMappings"/> + mapping-level NestedSchemaTypeName shape, and the
    /// mapping-level NestedSchemaTypeName is ignored.
    /// </summary>
    public List<BlockRoute>? Routes { get; set; }

    /// <summary>
    /// Legacy flat mappings list (back-compat). Applies to every block in the list,
    /// using the mapping-level NestedSchemaTypeName as the single implicit route's
    /// nested schema type.
    /// </summary>
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
/// A per-block-type route: maps one block element type to a specific Schema.org
/// nested type with its own property mappings. The TARGET page schema property is
/// the owning <see cref="Models.Entities.PropertyMapping.SchemaPropertyName"/> — the
/// multi-target fan-out (dominant block → mainEntity, the rest → hasPart/about) is
/// achieved by spreading routes across several property mappings, one per target.
/// </summary>
public class BlockRoute
{
    /// <summary>
    /// The block element type alias this route applies to. Empty matches any block.
    /// </summary>
    public string? BlockAlias { get; set; }

    /// <summary>
    /// The Schema.NET type to instantiate for blocks of this element type
    /// (e.g. "Question", "Person", "Place").
    /// </summary>
    public string? NestedSchemaType { get; set; }

    /// <summary>
    /// The property mappings to apply to the instantiated nested Thing. Uses the same
    /// shape as <see cref="NestedPropertyMapping"/>; the per-mapping BlockAlias is
    /// unused here because the route itself is already block-type scoped.
    /// </summary>
    public List<NestedPropertyMapping>? PropertyMappings { get; set; }
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

    /// <summary>
    /// Optional: when <see cref="ContentProperty"/> points at a block element property that is
    /// itself a Block List/Grid (a block nested inside a block), these routes map that nested
    /// block's element types to their own Schema.NET types — exactly like the top-level
    /// <see cref="BlockRoute"/> list. Resolution recurses (depth-capped) and the resulting
    /// nested Things are assigned to <see cref="SchemaProperty"/>.
    /// </summary>
    public List<BlockRoute>? Routes { get; set; }

    /// <summary>
    /// Optional: when <see cref="ContentProperty"/> points at a nested Block List/Grid and this
    /// is "stringList", the nested blocks are extracted as a string array (using
    /// <see cref="NestedContentProperty"/> as the inner block property) instead of Things —
    /// e.g. nested "ingredient" blocks feeding a recipeIngredient string[].
    /// </summary>
    public string? ExtractAs { get; set; }

    /// <summary>
    /// The inner block property alias to read when <see cref="ExtractAs"/> is "stringList".
    /// </summary>
    public string? NestedContentProperty { get; set; }
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
