using System.Text.Json;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;

namespace Umbraco.Community.SchemeWeaver.Services.Resolvers;

/// <summary>
/// Optional per-row configuration for picked-content resolution, carried in the
/// mapping's ResolverConfig JSON. <see cref="PickedPropertyAlias"/> switches the
/// row into single-property drill-down mode; <see cref="PickedContentTypeAlias"/>
/// is a UI-only hint (which document type's properties to list in the picker) and
/// is never consulted at render time — the property is read off whatever node was
/// actually picked.
/// </summary>
internal sealed class PickedItemConfig
{
    public string? PickedPropertyAlias { get; set; }
    public string? PickedContentTypeAlias { get; set; }

    /// <summary>
    /// Whole-item nesting type for a picker used as a complexType SUB-ROW. A top-level row carries
    /// this in the <c>PropertyMapping.NestedSchemaTypeName</c> column, but a
    /// <c>ComplexTypeMappingEntry</c> has no such column — and a new sibling field on the entry would
    /// be silently stripped by the backoffice complex-type modal on every re-save, which rebuilds
    /// each entry from a fixed key whitelist. <c>ResolverConfig</c> is round-tripped verbatim, so the
    /// type name travels inside it and is hoisted onto the synthetic mapping by
    /// <c>JsonLdGenerator.ResolveComplexTypePropertyValue</c>.
    /// </summary>
    public string? NestedSchemaTypeName { get; set; }

    /// <summary>
    /// Per-usage inline object built FROM the picked node: the same config shape a
    /// <c>complexType</c> row carries, but its sub-rows read the picked node's properties rather
    /// than the page's. Deliberately nested under its own key instead of hoisting
    /// <c>complexTypeMappings</c> to the top level — that key already means "resolve against THIS
    /// node" for complexType/blockContent rows, and reusing it would let a source-type switch carry
    /// the object onto a row that silently re-bases it to the page (the very confusion
    /// <c>applySourceTypeChange</c>'s config guard exists to prevent). The key names the base node.
    /// </summary>
    public ComplexTypeConfigModel? PickedComplexType { get; set; }
}

/// <summary>
/// Shared resolution logic for property editors whose value is picked content
/// (<see cref="ContentPickerResolver"/> and <see cref="MultiNodeTreePickerResolver"/>).
///
/// Precedence per picked item:
///   1. Drill-down — a configured <see cref="PickedItemConfig.PickedPropertyAlias"/>
///      resolves that single property off the picked node through the resolver
///      pipeline (returns null, never the node name, when it yields nothing:
///      an explicit drill-down should not silently emit unrelated data).
///   2. Per-usage object — a configured <see cref="PickedItemConfig.PickedComplexType"/>
///      builds a Thing from this row's own sub-mappings, resolved against the picked
///      node (returns null, never the node name, when it yields nothing).
///   3. Whole-item nesting — <c>Mapping.NestedSchemaTypeName</c> plus a saved
///      SchemaMapping on the picked node's own content type renders the node as
///      a nested Thing.
///   4. Fallback — the picked node's Name.
/// </summary>
internal static class PickedContentResolver
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Parses the drill-down config out of a mapping's ResolverConfig. Returns null
    /// for absent/invalid JSON — resolution then falls back to the normal ladder,
    /// per the never-break-the-page policy.
    /// </summary>
    internal static PickedItemConfig? ParseConfig(string? resolverConfig)
    {
        if (string.IsNullOrWhiteSpace(resolverConfig))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PickedItemConfig>(resolverConfig, ConfigJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves one picked node through the precedence ladder documented on the class.
    /// </summary>
    internal static object? ResolveItem(IPublishedContent pickedContent, PropertyResolverContext context, PickedItemConfig? config)
    {
        if (!string.IsNullOrWhiteSpace(config?.PickedPropertyAlias))
            return ResolvePickedProperty(pickedContent, config.PickedPropertyAlias, context);

        if (config?.PickedComplexType?.ComplexTypeMappings is { Count: > 0 })
        {
            var customObject = BuildPickedComplexType(pickedContent, context, config.PickedComplexType);
            if (customObject is not null)
                return customObject;

            // An explicitly configured per-usage object that resolved nothing must NOT degrade to
            // the node's name: the editor chose an object shape for this property, and substituting
            // a bare string would either be dropped by the setter or auto-wrapped into a fabricated
            // Thing. Same reasoning as the drill rung above.
            return null;
        }

        if (!string.IsNullOrEmpty(context.Mapping.NestedSchemaTypeName)
            && context.RecursionDepth < context.MaxRecursionDepth
            && !context.VisitedContentKeys.Contains(pickedContent.Key))
        {
            var nestedThing = GenerateNestedThing(pickedContent, context);
            if (nestedThing is not null)
                return nestedThing;
        }

        return pickedContent.Name;
    }

    /// <summary>
    /// Drill-down: resolves a single property off the picked node through the resolver
    /// pipeline, so media pickers become ImageObjects, dates format correctly, and the
    /// built-in aliases (__name, __url, …) resolve against the picked node.
    /// </summary>
    internal static object? ResolvePickedProperty(IPublishedContent pickedContent, string propertyAlias, PropertyResolverContext context)
    {
        if (context.ResolverFactory is null)
        {
            // No pipeline available (legacy/test paths): plain value extraction.
            return SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propertyAlias)
                ? null
                : SchemaPropertySetter.ResolveElementPropertyValue(
                    pickedContent, propertyAlias, context.HttpContextAccessor);
        }

        if (context.RecursionDepth >= context.MaxRecursionDepth)
            return null;

        IPublishedProperty? publishedProperty = null;
        string? editorAlias;

        if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propertyAlias))
        {
            editorAlias = SchemeWeaverConstants.BuiltInProperties.EditorAlias;
        }
        else
        {
            publishedProperty = pickedContent.GetProperty(propertyAlias);
            if (publishedProperty is null)
                return null;

            editorAlias = publishedProperty.PropertyType?.EditorAlias;
        }

        var resolver = context.ResolverFactory.GetResolver(editorAlias);
        var childContext = CreateChildContext(pickedContent, propertyAlias, publishedProperty, context);

        return resolver.Resolve(childContext);
    }

    /// <summary>
    /// Per-usage inline object: builds a Schema.org Thing from this row's own sub-mappings, resolved
    /// against the PICKED node. Unlike whole-item nesting the picked type needs no saved mapping of
    /// its own, so the same picked type can be shaped differently per usage.
    ///
    /// The visited chain is extended with the HOST node before handing off; the builder checks and
    /// then adds the picked node itself. That is what makes A-picks-B-picks-A terminate: B's object
    /// sees A already in the chain and degrades to A's name instead of recursing.
    /// </summary>
    private static Thing? BuildPickedComplexType(
        IPublishedContent pickedContent, PropertyResolverContext context, ComplexTypeConfigModel config)
    {
        if (context.ComplexTypeBuilder is null)
            return null; // legacy/test path without the builder — degrade, never throw

        var typeName = !string.IsNullOrWhiteSpace(context.Mapping.NestedSchemaTypeName)
            ? context.Mapping.NestedSchemaTypeName!
            : config.SelectedSubType;

        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var visited = new HashSet<Guid>(context.VisitedContentKeys) { context.Content.Key };

        return context.ComplexTypeBuilder.BuildFromConfig(
            typeName, config, pickedContent, context.Culture,
            context.RecursionDepth + 1, visited);
    }

    /// <summary>
    /// Whole-item nesting: renders the picked node as a Thing by replaying its own
    /// content type's saved SchemaMapping through the resolver pipeline.
    /// </summary>
    internal static Thing? GenerateNestedThing(IPublishedContent content, PropertyResolverContext context)
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

        foreach (var propMapping in propertyMappings
            .Where(pm => !string.IsNullOrEmpty(pm.ContentTypePropertyAlias)))
        {
            var alias = propMapping.ContentTypePropertyAlias!;

            // Use the resolver pipeline when available so nested content pickers,
            // media pickers, built-ins etc. are resolved correctly with depth/cycle tracking
            if (context.ResolverFactory is not null)
            {
                IPublishedProperty? publishedProperty = null;
                string? editorAlias;

                if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(alias))
                {
                    editorAlias = SchemeWeaverConstants.BuiltInProperties.EditorAlias;
                }
                else
                {
                    publishedProperty = content.GetProperty(alias);
                    editorAlias = publishedProperty?.PropertyType?.EditorAlias;
                }

                var resolver = context.ResolverFactory.GetResolver(editorAlias);
                var childContext = CreateChildContext(content, alias, publishedProperty, context, propMapping);

                var resolvedValue = resolver.Resolve(childContext);
                if (resolvedValue is null)
                    continue;

                SchemaPropertySetter.SetPropertyValue(instance, propMapping.SchemaPropertyName, resolvedValue);
            }
            else
            {
                // Fallback: simple value extraction without the resolver pipeline
                var resolvedValue = SchemaPropertySetter.ResolveElementPropertyValue(
                    content, alias, context.HttpContextAccessor);
                if (resolvedValue is null)
                    continue;

                SchemaPropertySetter.SetPropertyValue(instance, propMapping.SchemaPropertyName, resolvedValue);
            }
        }

        // Empty-shell guard, matching complexType (JsonLdGenerator.ResolveComplexTypeFromConfig) and
        // blockContent (BlockContentResolver.MapBlockToThing): when the picked type's mapping landed
        // nothing — no saved rows resolved, or every set was dropped because the outer row's
        // NestedSchemaTypeName disagrees with the picked type's own schema type — a {"@type":"Person"}
        // shell is invalid structured data. Returning null lets ResolveItem fall through to the
        // picked node's name, which is at least true.
        return SchemaPropertySetter.HasResolvedProperty(instance) ? instance : null;
    }

    /// <summary>
    /// Builds the child resolution context for a property on the picked node. The
    /// mapping is synthetic (or the picked type's own row) — never the outer row —
    /// so the outer NestedSchemaTypeName/ResolverConfig cannot leak into nested
    /// resolution and trigger re-entrant drilling.
    /// </summary>
    private static PropertyResolverContext CreateChildContext(
        IPublishedContent content,
        string propertyAlias,
        IPublishedProperty? publishedProperty,
        PropertyResolverContext context,
        PropertyMapping? mapping = null)
    {
        return new PropertyResolverContext
        {
            Content = content,
            Mapping = mapping ?? new PropertyMapping
            {
                ContentTypePropertyAlias = propertyAlias,
                SourceType = "property"
            },
            PropertyAlias = propertyAlias,
            SchemaTypeRegistry = context.SchemaTypeRegistry,
            MappingRepository = context.MappingRepository,
            HttpContextAccessor = context.HttpContextAccessor,
            ResolverFactory = context.ResolverFactory,
            ComplexTypeBuilder = context.ComplexTypeBuilder,
            Property = publishedProperty,
            RecursionDepth = context.RecursionDepth + 1,
            MaxRecursionDepth = context.MaxRecursionDepth,
            VisitedContentKeys = new HashSet<Guid>(context.VisitedContentKeys) { context.Content.Key },
            Culture = context.Culture
        };
    }
}
