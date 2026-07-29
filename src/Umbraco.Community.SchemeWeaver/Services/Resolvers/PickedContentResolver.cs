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
///   2. Whole-item nesting — <c>Mapping.NestedSchemaTypeName</c> plus a saved
///      SchemaMapping on the picked node's own content type renders the node as
///      a nested Thing.
///   3. Fallback — the picked node's Name.
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

        return instance;
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
            Property = publishedProperty,
            RecursionDepth = context.RecursionDepth + 1,
            MaxRecursionDepth = context.MaxRecursionDepth,
            VisitedContentKeys = new HashSet<Guid>(context.VisitedContentKeys) { context.Content.Key },
            Culture = context.Culture
        };
    }
}
