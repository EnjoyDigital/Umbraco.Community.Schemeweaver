using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Services.Transforms;

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
                if (rawValue is not string s || string.IsNullOrEmpty(s))
                    continue;

                // Honour a transform on the string-list path too (e.g. stripHtml a RichText
                // value pulled via extractAs:stringList). Re-check for emptiness after the
                // transform so a value that collapses to whitespace is dropped, not emitted blank.
                if (!string.IsNullOrEmpty(resolverConfig.TransformType))
                    s = SchemaValueTransformer.Apply(s, resolverConfig.TransformType, context.HttpContextAccessor, _logger) ?? string.Empty;

                if (!string.IsNullOrEmpty(s))
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
            // Keep each block paired with its mapped Thing so empty Things are dropped
            // (P2.1) BEFORE ListItem position numbering (P2.3), keeping positions sequential.
            var routed = blockItems
                .Select(blockContent => (Block: blockContent, Thing: MapBlockViaRoute(blockContent, routes, context)))
                .Where(x => x.Thing is not null)
                .Select(x => (x.Block, Thing: x.Thing!))
                .ToList();

            return BuildBlockResult(routed, resolverConfig, context);
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
            .Select(blockContent => (Block: blockContent,
                Thing: MapBlockToThing(blockContent, nestedSchemaTypeName, resolverConfig?.NestedMappings, context, resolverConfig?.RequiredProperties)))
            .Where(x => x.Thing is not null)
            .Select(x => (x.Block, Thing: x.Thing!))
            .ToList();

        return BuildBlockResult(things, resolverConfig, context);
    }

    /// <summary>
    /// Builds the final resolver result from the mapped (block, Thing) pairs: either a bare
    /// <c>List&lt;Thing&gt;</c> (default) or, when <see cref="ResolverConfigModel.WrapInListItem"/>
    /// is set, an ordered <c>List&lt;ListItem&gt;</c> (P2.3). Returns null when nothing mapped.
    /// </summary>
    private static object? BuildBlockResult(
        List<(IPublishedElement Block, Thing Thing)> mapped,
        ResolverConfigModel? config,
        PropertyResolverContext context)
    {
        if (mapped.Count == 0)
            return null;

        if (config?.WrapInListItem == true)
            return WrapAsListItems(mapped, config, context);

        return mapped.Select(x => x.Thing).ToList();
    }

    /// <summary>
    /// Wraps each mapped block Thing in a <see cref="ListItem"/> with a 1-based
    /// <see cref="ListItem.Position"/> (or a value read from
    /// <see cref="ResolverConfigModel.PositionProperty"/> when configured) and the Thing under
    /// <see cref="ListItem.Item"/>. The resulting collection is assigned to the owning mapping's
    /// schema property (e.g. an <c>ItemList.itemListElement</c>); the parent <c>ItemList</c> type
    /// comes from the page mapping, not this resolver.
    /// </summary>
    private static List<ListItem> WrapAsListItems(
        List<(IPublishedElement Block, Thing Thing)> mapped,
        ResolverConfigModel config,
        PropertyResolverContext context)
    {
        var items = new List<ListItem>(mapped.Count);
        for (var i = 0; i < mapped.Count; i++)
        {
            var listItem = new ListItem { Position = i + 1 };

            if (!string.IsNullOrEmpty(config.PositionProperty))
            {
                var raw = SchemaPropertySetter.ResolveElementPropertyValue(
                    mapped[i].Block, config.PositionProperty, context.HttpContextAccessor);
                if (raw is string s && int.TryParse(s, out var explicitPosition))
                    listItem.Position = explicitPosition;
                else if (raw is int p)
                    listItem.Position = p;
            }

            // Use the setter so the base Thing is converted into OneOrMany<IThing> robustly.
            SchemaPropertySetter.SetPropertyValue(listItem, "Item", mapped[i].Thing);
            items.Add(listItem);
        }

        return items;
    }

    /// <summary>
    /// Renders a single block element instance to a Schema.NET Thing using a supplied route —
    /// the faithful per-block rendering (wrapping, transforms, empty-drop, nested-route recursion)
    /// exactly as it would emit inside its page. Used by block-instance preview. Returns null when
    /// the route has no nested schema type or the element resolves no usable properties (P2.1).
    /// </summary>
    public Thing? MapElementViaRoute(IPublishedElement element, BlockRoute route, PropertyResolverContext context)
    {
        if (route is null || string.IsNullOrEmpty(route.NestedSchemaType))
            return null;

        return MapBlockToThing(element, route.NestedSchemaType, route.PropertyMappings, context, route.RequiredProperties);
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

        return MapBlockToThing(blockContent, route.NestedSchemaType, route.PropertyMappings, context, route.RequiredProperties);
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
        PropertyResolverContext context,
        List<string>? requiredProperties = null)
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

        // P2.1: drop a nested Thing that resolved no usable (non-@type/@id) property, so a blank
        // block row never emits an empty typed node (e.g. a Question with no name/acceptedAnswer,
        // which Google rejects). When RequiredProperties is configured, every named property must
        // also resolve.
        if (!HasResolvedProperty(instance))
            return null;

        if (requiredProperties is { Count: > 0 } &&
            requiredProperties.Any(p => !HasNamedProperty(instance, p)))
            return null;

        return instance;
    }

    /// <summary>
    /// True when <paramref name="thing"/> has at least one resolved Schema.org value property.
    /// Only properties whose type implements <see cref="IValues"/> (every <c>OneOrMany</c>/
    /// <c>Values</c> wrapper) with <c>Count &gt; 0</c> count — which cleanly excludes the
    /// <c>@type</c> (string), <c>@id</c> (Uri) and <c>@context</c> identity members and mirrors
    /// Schema.NET's own serializer, so "has a resolved property" ⇔ "will emit a property".
    /// </summary>
    private static bool HasResolvedProperty(Thing thing)
    {
        foreach (var p in thing.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0)
                continue;
            if (p.GetValue(thing) is IValues { Count: > 0 })
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when the named schema property (case-insensitive) is set to a non-empty
    /// <see cref="IValues"/> on <paramref name="thing"/>. Used for route RequiredProperties.
    /// </summary>
    private static bool HasNamedProperty(Thing thing, string schemaPropertyName)
    {
        foreach (var p in thing.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0)
                continue;
            if (!string.Equals(p.Name, schemaPropertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            return p.GetValue(thing) is IValues { Count: > 0 };
        }

        return false;
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

            // P2.2: apply the nested transform before the empty guard (same as MapPropertyFromConfig).
            if (rawValue is string toTransform && !string.IsNullOrEmpty(mapping.TransformType))
                rawValue = SchemaValueTransformer.Apply(toTransform, mapping.TransformType, context.HttpContextAccessor);

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

        // P2.2: apply the nested transform (stripHtml/toAbsoluteUrl/formatDate) before the empty
        // guard, so e.g. stripHtml collapsing "<p></p>" to "" is then dropped (composes with P2.1).
        if (rawValue is string toTransform && !string.IsNullOrEmpty(mapping.TransformType))
            rawValue = SchemaValueTransformer.Apply(toTransform, mapping.TransformType, context.HttpContextAccessor, _logger);

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
    /// When a <see cref="PropertyResolverContext.ResolverFactory"/> is available the block element
    /// property is routed through the per-editor resolver pipeline (so e.g. a block media property
    /// flows through <c>MediaPickerResolver</c> and returns a Schema.NET <c>ImageObject</c> rather
    /// than hitting the raw-JSON fallback). The child context keeps <see cref="PropertyResolverContext.Content"/>
    /// as the PAGE node and carries the block element property under
    /// <see cref="PropertyResolverContext.Property"/>. Falls back to the static
    /// <see cref="SchemaPropertySetter.ResolveElementPropertyValue"/> helper when no factory is
    /// supplied (keeps unit tests that don't wire a factory green) or when the factory yields no
    /// resolver for the editor alias.
    /// </summary>
    private static object? ResolveBlockElementProperty(
        IPublishedElement blockContent,
        string propertyAlias,
        PropertyResolverContext context)
    {
        if (context.ResolverFactory is { } factory)
        {
            var prop = blockContent.GetProperty(propertyAlias);
            if (prop is null)
                return null;

            // Route through the per-editor resolver factory so the block element property flows
            // through the same pipeline as top-level properties (media pickers, dates, etc.).
            IPropertyValueResolver? resolver = factory.GetResolver(prop.PropertyType?.EditorAlias);
            if (resolver is not null)
            {
                var childContext = new PropertyResolverContext
                {
                    Content = context.Content,                 // stays the PAGE node
                    Mapping = context.Mapping,
                    PropertyAlias = propertyAlias,
                    SchemaTypeRegistry = context.SchemaTypeRegistry,
                    MappingRepository = context.MappingRepository,
                    HttpContextAccessor = context.HttpContextAccessor,
                    ResolverFactory = factory,
                    Property = prop,                           // the block element property
                    RecursionDepth = context.RecursionDepth,
                    MaxRecursionDepth = context.MaxRecursionDepth,
                    VisitedContentKeys = context.VisitedContentKeys,
                    Culture = context.Culture
                };

                return resolver.Resolve(childContext);
            }
        }

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

        // Propagate the wrap/position/transform config one level deeper. Previously only Routes
        // (or the stringList pair) were copied, so a nested ItemList could not emit ListItems and a
        // nested stringList could not be transformed — the child config silently lost those fields.
        var nestedConfig = mapping.ExtractAs == "stringList"
            ? new ResolverConfigModel
            {
                ExtractAs = "stringList",
                ContentProperty = mapping.NestedContentProperty,
                TransformType = mapping.TransformType,
            }
            : new ResolverConfigModel
            {
                Routes = mapping.Routes,
                WrapInListItem = mapping.WrapInListItem,
                PositionProperty = mapping.PositionProperty,
            };

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

    /// <summary>
    /// Legacy-path equivalent of <see cref="BlockRoute.RequiredProperties"/>: schema property
    /// names that must resolve for a nested Thing to be emitted (P2.1).
    /// </summary>
    public List<string>? RequiredProperties { get; set; }

    /// <summary>
    /// P2.3 (opt-in): when true, each mapped block is wrapped in a <see cref="Schema.NET.ListItem"/>
    /// with an auto-incremented <c>position</c> and the Thing under <c>item</c>, producing an ordered
    /// list suitable for an <c>ItemList.itemListElement</c>. Default false keeps the bare Thing array
    /// (no breaking change).
    /// </summary>
    public bool WrapInListItem { get; set; }

    /// <summary>
    /// P2.3 (optional): a block element property holding an explicit position; when set it overrides
    /// the auto-incremented position. Falls back to 1-based sequence order when absent or unparseable.
    /// </summary>
    public string? PositionProperty { get; set; }

    /// <summary>
    /// Optional value transform applied to each extracted string when <see cref="ExtractAs"/> is
    /// "stringList" — the same set <see cref="SchemaValueTransformer"/> supports (e.g. <c>stripHtml</c>
    /// a RichText value pulled into a string array). Ignored outside the string-list path.
    /// </summary>
    public string? TransformType { get; set; }
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

    /// <summary>
    /// Optional: schema property names that MUST resolve to a value for the nested Thing to be
    /// emitted. When set, a Thing missing any of these is dropped (P2.1) — e.g. require
    /// <c>name</c> and <c>acceptedAnswer</c> on a <c>Question</c>. When unset, the Thing is kept
    /// as long as it resolved at least one property.
    /// </summary>
    public List<string>? RequiredProperties { get; set; }
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
    /// Optional value transform applied to the resolved string before it is set:
    /// <c>stripHtml</c>, <c>toAbsoluteUrl</c>, or <c>formatDate</c> — the same set the
    /// top-level <see cref="Models.Entities.PropertyMapping.TransformType"/> supports. Applied
    /// before the empty-value guard, so a transform that collapses to an empty string drops the
    /// value (and, via P2.1, a now-empty nested Thing).
    /// </summary>
    public string? TransformType { get; set; }

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

    /// <summary>
    /// P2.3 (opt-in) for a NESTED block list: when true, the nested blocks resolved for this
    /// mapping (via <see cref="Routes"/>) are wrapped as <see cref="Schema.NET.ListItem"/>s with a
    /// position, exactly like the top-level <see cref="ResolverConfigModel.WrapInListItem"/>. Needed
    /// because wrapping config previously did not propagate past the first nesting level — a list
    /// nested inside an ItemList (e.g. services under an ItemList) stayed a bare Thing array.
    /// </summary>
    public bool WrapInListItem { get; set; }

    /// <summary>
    /// Optional explicit-position block property for the nested wrap, mirroring
    /// <see cref="ResolverConfigModel.PositionProperty"/>. Falls back to 1-based sequence order.
    /// </summary>
    public string? PositionProperty { get; set; }
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

    /// <summary>
    /// Optional value transform applied to a resolved <c>property</c> sub-value before it is set on
    /// the nested complex Thing — the same set <see cref="SchemaValueTransformer"/> supports (e.g.
    /// <c>stripHtml</c> a RichText sub-property). <c>static</c> sub-values are not transformed,
    /// mirroring the top-level static behaviour.
    /// </summary>
    public string? TransformType { get; set; }
}
