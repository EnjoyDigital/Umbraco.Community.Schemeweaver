using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;
using Umbraco.Community.SchemeWeaver.Services.Transforms;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Generates JSON-LD from published content using stored schema mappings.
/// Uses the extensible <see cref="IPropertyValueResolver"/> pattern for property value extraction.
/// </summary>
public partial class JsonLdGenerator : IJsonLdGenerator
{
    private readonly ISchemaMappingRepository _repository;
    private readonly ISchemaTypeRegistry _registry;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDocumentNavigationQueryService _navigationQueryService;
    private readonly IPublishedContentStatusFilteringService _publishedStatusFilteringService;
    private readonly IPropertyValueResolverFactory _resolverFactory;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly IVariationContextAccessor _variationContextAccessor;
    private readonly ILogger<JsonLdGenerator> _logger;
    private readonly SchemeWeaverOptions _options;

    /// <summary>
    /// Fallback serialiser options for Schema.NET types that have property name collisions
    /// (e.g. Drug, MedicalCondition, Physician — ~83 types in Schema.NET 13.0.0).
    /// Only used when <see cref="Thing.ToString()"/> throws.
    /// </summary>
    private static readonly JsonSerializerOptions _deduplicatingOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        TypeInfoResolver = new DeduplicatingTypeInfoResolver()
    };

    public JsonLdGenerator(
        ISchemaMappingRepository repository,
        ISchemaTypeRegistry registry,
        IHttpContextAccessor httpContextAccessor,
        IDocumentNavigationQueryService navigationQueryService,
        IPublishedContentStatusFilteringService publishedStatusFilteringService,
        IPropertyValueResolverFactory resolverFactory,
        IPublishedUrlProvider urlProvider,
        IVariationContextAccessor variationContextAccessor,
        ILogger<JsonLdGenerator> logger,
        IOptions<SchemeWeaverOptions> options)
    {
        _repository = repository;
        _registry = registry;
        _httpContextAccessor = httpContextAccessor;
        _navigationQueryService = navigationQueryService;
        _publishedStatusFilteringService = publishedStatusFilteringService;
        _resolverFactory = resolverFactory;
        _urlProvider = urlProvider;
        _variationContextAccessor = variationContextAccessor;
        _logger = logger;
        _options = options.Value;
    }

    public Thing? GenerateJsonLd(
        IPublishedContent content,
        string? culture = null,
        GraphPieceContext? graphContext = null)
    {
        var previousContext = _variationContextAccessor.VariationContext;
        if (culture is not null)
            _variationContextAccessor.VariationContext = new VariationContext(culture);

        try
        {
            return GenerateJsonLdCore(content, culture, graphContext);
        }
        finally
        {
            _variationContextAccessor.VariationContext = previousContext;
        }
    }

    private Thing? GenerateJsonLdCore(IPublishedContent content, string? culture, GraphPieceContext? graphContext = null)
    {
        var mapping = _repository.GetByContentTypeAlias(content.ContentType.Alias);
        if (mapping is not { IsEnabled: true })
            return null;

        var clrType = _registry.GetClrType(mapping.SchemaTypeName);
        if (clrType is null)
        {
            _logger.LogWarning("Schema type {TypeName} not found in registry", mapping.SchemaTypeName);
            return null;
        }

        if (Activator.CreateInstance(clrType) is not Thing instance)
            return null;

        var propertyMappings = _repository.GetPropertyMappings(mapping.Id);
        var hasExplicitInLanguage = false;
        var hasExplicitId = false;

        foreach (var propMapping in propertyMappings)
        {
            try
            {
                if (string.Equals(propMapping.SchemaPropertyName, "InLanguage", StringComparison.OrdinalIgnoreCase))
                    hasExplicitInLanguage = true;

                // `reference` source type: cross-piece @id ref. Only resolvable
                // when called inside the graph pipeline (graphContext != null).
                // Outside that pipeline (legacy single-Thing callers) we can't
                // know what to point at, so the mapping is skipped.
                if (string.Equals(propMapping.SourceType, "reference", StringComparison.OrdinalIgnoreCase))
                {
                    if (graphContext is null || string.IsNullOrWhiteSpace(propMapping.TargetPieceKey))
                        continue;

                    var refId = graphContext.IdFor(propMapping.TargetPieceKey);
                    if (string.IsNullOrWhiteSpace(refId)
                        || !Uri.TryCreate(refId, UriKind.Absolute, out var refUri))
                        continue;

                    // Thing shell with only @id — GraphGenerator's ref-collapse
                    // will reduce the serialised form to {"@id": "..."}. Works
                    // for any target property that accepts a Thing-typed value,
                    // which covers all cross-entity links in Schema.org.
                    SchemaPropertySetter.SetPropertyValue(
                        instance,
                        propMapping.SchemaPropertyName,
                        new Thing { Id = refUri });
                    continue;
                }

                var value = ResolveValue(propMapping, content, culture);
                if (value is null)
                    continue;

                // Apply transforms only to string values; skip empty/whitespace
                if (value is string stringValue)
                {
                    if (string.IsNullOrWhiteSpace(stringValue))
                        continue;
                    value = ApplyTransform(stringValue, propMapping.TransformType);
                }

                // Guard against null after transform (ApplyTransform can return null)
                if (value is null or string { Length: 0 })
                    continue;

                // @id is Uri-typed on Schema.NET Thing; SchemaPropertySetter can't convert a
                // string to Uri generically, so we handle it here. Setting via mapping
                // suppresses the default {url}#{type} convention below.
                if (string.Equals(propMapping.SchemaPropertyName, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryCoerceToUri(value, out var idUri))
                    {
                        instance.Id = idUri;
                        hasExplicitId = true;
                    }
                    continue;
                }

                SchemaPropertySetter.SetPropertyValue(instance, propMapping.SchemaPropertyName, value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to map property {Property} for content {ContentId}",
                    propMapping.SchemaPropertyName, content.Id);
            }
        }

        // Auto-fill inLanguage when culture is set and not explicitly mapped
        if (culture is not null && !hasExplicitInLanguage)
            SchemaPropertySetter.SetPropertyValue(instance, "InLanguage", culture);

        // @id precedence:
        //   1. Explicit "Id" property mapping (handled in the loop above, sets hasExplicitId).
        //   2. Mapping-level IdOverride template with token expansion.
        //   3. Default {absoluteUrl}#{schemaTypeLowercase} — disambiguates the page
        //      (WebPage) from any other entity it describes (Organization, Article, …)
        //      that would otherwise collide on the bare URL.
        if (!hasExplicitId)
        {
            var contentUrl = ResolveAbsoluteUrl(content);
            var expandedId = ResolveIdFromOverrideOrDefault(mapping, content, contentUrl, culture);
            if (!string.IsNullOrEmpty(expandedId)
                && Uri.TryCreate(expandedId, UriKind.Absolute, out var idUri))
            {
                instance.Id = idUri;
            }
        }

        return instance;
    }

    public string? GenerateJsonLdString(IPublishedContent content, string? culture = null)
    {
        var thing = GenerateJsonLd(content, culture);
        return SafeSerialize(thing, content.Id);
    }

    public string? GenerateBreadcrumbJsonLd(IPublishedContent content, string? culture = null)
    {
        // Walk the parent chain to build the ancestor list (root-first order)
        var ancestors = new List<IPublishedContent> { content };
        try
        {
            var current = content.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
            while (current is not null)
            {
                ancestors.Add(current);
                current = current.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to walk parent chain for breadcrumb generation on content {ContentId}", content.Id);
            return null;
        }
        ancestors.Reverse();

        return BuildBreadcrumbJsonLd(ancestors);
    }

    /// <summary>
    /// Builds a BreadcrumbList JSON-LD string from a root-first ordered list of content nodes.
    /// Returns null if the list has fewer than 2 items (no meaningful breadcrumb trail).
    /// </summary>
    internal string? BuildBreadcrumbJsonLd(List<IPublishedContent> ancestors)
    {
        if (ancestors.Count <= 1)
            return null; // No breadcrumbs for root nodes

        var breadcrumb = new BreadcrumbList();
        var items = new List<IListItem>();

        for (var i = 0; i < ancestors.Count; i++)
        {
            var ancestor = ancestors[i];
            var url = ResolveAbsoluteUrl(ancestor);

            var listItem = new ListItem
            {
                Position = i + 1,
                Name = ancestor.Name
            };

            if (!string.IsNullOrEmpty(url)
                && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Google's BreadcrumbList spec requires `item` on each ListItem
                // (not `url`/`@id`). A minimal WebPage with @id is accepted.
                listItem.Item = new WebPage { Id = uri };
            }

            items.Add(listItem);
        }

        breadcrumb.ItemListElement = items;
        return SafeSerialize(breadcrumb);
    }

    /// <summary>
    /// Resolves an absolute URL for content using the URL provider with a request-context fallback.
    /// </summary>
    /// <summary>
    /// Coerces a resolved mapping value to a Uri suitable for Schema.NET Thing.Id.
    /// Accepts Uri directly or any string parseable as relative-or-absolute URI.
    /// Relative Uris (e.g. "#organization") are preserved as-is so users can express
    /// fragment-only identifiers.
    /// </summary>
    private static bool TryCoerceToUri(object value, out Uri uri)
    {
        switch (value)
        {
            case Uri u:
                uri = u;
                return true;
            case string s when !string.IsNullOrWhiteSpace(s)
                               && Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out var parsed):
                uri = parsed;
                return true;
            default:
                uri = null!;
                return false;
        }
    }

    private string? ResolveAbsoluteUrl(IPublishedContent content)
    {
        var url = _urlProvider.GetUrl(content, UrlMode.Absolute);
        if (!string.IsNullOrEmpty(url) && url != "#")
            return url;

        // Fallback: build absolute URL from relative + request context
        var relativeUrl = _urlProvider.GetUrl(content, UrlMode.Relative);
        if (string.IsNullOrEmpty(relativeUrl) || relativeUrl == "#")
            return null;

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
            return null;

        return $"{request.Scheme}://{request.Host}{relativeUrl}";
    }

    private string? ResolveSiteUrl()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is not null)
            return $"{request.Scheme}://{request.Host}";
        return null;
    }

    /// <inheritdoc />
    public string? GetResolvedBaseUrl() => ResolveSiteUrl();

    /// <inheritdoc />
    public IPublishedElement? FindBlockInstance(IPublishedContent page, Guid blockInstanceKey, string? culture = null)
    {
        var previousContext = _variationContextAccessor.VariationContext;
        if (culture is not null)
            _variationContextAccessor.VariationContext = new VariationContext(culture);
        try
        {
            return FindBlockInstanceCore(page, blockInstanceKey, culture);
        }
        finally
        {
            _variationContextAccessor.VariationContext = previousContext;
        }
    }

    private IPublishedElement? FindBlockInstanceCore(IPublishedContent page, Guid blockInstanceKey, string? culture)
    {
        foreach (var property in page.Properties
            .Where(p => p.PropertyType?.EditorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid"))
        {
            var value = property.GetValue(culture: culture);
            if (value is null)
                continue;

            var match = EnumerateNestedBlockElements(value, culture, 0, new HashSet<Guid>())
                .FirstOrDefault(e => e.Key == blockInstanceKey);
            if (match is not null)
                return match;
        }

        return null;
    }

    /// <inheritdoc />
    public BlockInstancePreviewResult GenerateBlockInstanceJsonLd(IPublishedContent page, Guid blockInstanceKey, string? culture = null)
    {
        var previousContext = _variationContextAccessor.VariationContext;
        if (culture is not null)
            _variationContextAccessor.VariationContext = new VariationContext(culture);
        try
        {
            var element = FindBlockInstanceCore(page, blockInstanceKey, culture);
            if (element is null)
                return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.BlockNotFound, null, page.Name, page.Key, null, null);

            var blockAlias = element.ContentType.Alias;
            var pageMapping = _repository.GetByContentTypeAlias(page.ContentType.Alias);
            var route = pageMapping is null
                ? null
                : FindRouteForBlock(_repository.GetPropertyMappings(pageMapping.Id).ToList(), blockAlias, depth: 0);

            if (route is null)
                return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.NoRouteForBlock, null, page.Name, page.Key, blockAlias, null);

            // The block-list resolver is the sole claimant of the Umbraco.BlockList alias.
            if (_resolverFactory.GetResolver("Umbraco.BlockList") is not BlockContentResolver blockResolver)
                return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.NoRouteForBlock, null, page.Name, page.Key, blockAlias, route.NestedSchemaType);

            var context = new PropertyResolverContext
            {
                Content = page,
                Mapping = new PropertyMapping { SourceType = "blockContent", SchemaPropertyName = route.NestedSchemaType ?? string.Empty },
                PropertyAlias = blockAlias,
                SchemaTypeRegistry = _registry,
                MappingRepository = _repository,
                HttpContextAccessor = _httpContextAccessor,
                ResolverFactory = _resolverFactory,
                RecursionDepth = 0,
                MaxRecursionDepth = _options.MaxRecursionDepth,
                Culture = culture,
                VisitedContentKeys = new HashSet<Guid> { element.Key }
            };

            var thing = blockResolver.MapElementViaRoute(element, route, context);
            if (thing is null)
                return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.EmptyAfterRender, null, page.Name, page.Key, blockAlias, route.NestedSchemaType);

            var json = SafeSerialize(thing);
            if (string.IsNullOrEmpty(json))
                return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.EmptyAfterRender, null, page.Name, page.Key, blockAlias, route.NestedSchemaType);

            return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.Rendered, json, page.Name, page.Key, blockAlias, route.NestedSchemaType);
        }
        finally
        {
            _variationContextAccessor.VariationContext = previousContext;
        }
    }

    /// <summary>
    /// Finds the <see cref="BlockRoute"/> on the page mapping that maps a block of type
    /// <paramref name="blockAlias"/> to a schema type. Searches every <c>blockContent</c> property
    /// mapping's routes, recursing into nested routes (a block nested inside another block) up to the
    /// configured depth. Exact alias match wins; otherwise an empty-alias wildcard; legacy
    /// <c>NestedMappings</c> config is treated as an implicit wildcard route.
    /// </summary>
    private BlockRoute? FindRouteForBlock(IReadOnlyList<PropertyMapping> pagePropertyMappings, string blockAlias, int depth)
    {
        BlockRoute? wildcard = null;

        foreach (var pm in pagePropertyMappings.Where(p => p.SourceType == "blockContent"))
        {
            var config = ParseResolverConfigModel(pm.ResolverConfig);
            if (config is null)
                continue;

            if (config.Routes is { Count: > 0 } routes)
            {
                var (exact, wc) = SearchRoutesForBlock(routes, blockAlias, depth);
                if (exact is not null)
                    return exact;
                wildcard ??= wc;
            }
            else if (config.NestedMappings is { Count: > 0 } && !string.IsNullOrEmpty(pm.NestedSchemaTypeName))
            {
                wildcard ??= new BlockRoute
                {
                    BlockAlias = string.Empty,
                    NestedSchemaType = pm.NestedSchemaTypeName,
                    PropertyMappings = config.NestedMappings,
                    RequiredProperties = config.RequiredProperties
                };
            }
        }

        return wildcard;
    }

    private (BlockRoute? Exact, BlockRoute? Wildcard) SearchRoutesForBlock(List<BlockRoute> routes, string blockAlias, int depth)
    {
        if (depth > _options.MaxRecursionDepth)
            return (null, null);

        BlockRoute? wildcard = null;
        foreach (var route in routes)
        {
            if (string.IsNullOrEmpty(route.NestedSchemaType))
                continue;

            if (string.Equals(route.BlockAlias, blockAlias, StringComparison.OrdinalIgnoreCase))
                return (route, null);

            if (string.IsNullOrEmpty(route.BlockAlias))
                wildcard ??= route;

            if (route.PropertyMappings is { Count: > 0 } nested)
            {
                foreach (var npm in nested.Where(m => m.Routes is { Count: > 0 }))
                {
                    var (nestedExact, nestedWc) = SearchRoutesForBlock(npm.Routes!, blockAlias, depth + 1);
                    if (nestedExact is not null)
                        return (nestedExact, null);
                    wildcard ??= nestedWc;
                }
            }
        }

        return (null, wildcard);
    }

    private static ResolverConfigModel? ParseResolverConfigModel(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ResolverConfigModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? ResolveIdFromOverrideOrDefault(
        SchemaMapping mapping,
        IPublishedContent content,
        string? contentUrl,
        string? culture)
    {
        if (!string.IsNullOrWhiteSpace(mapping.IdOverride))
        {
            var expanded = ExpandIdTokens(
                mapping.IdOverride,
                contentUrl,
                ResolveSiteUrl(),
                mapping.SchemaTypeName,
                content.Key,
                culture);
            if (!string.IsNullOrWhiteSpace(expanded))
                return expanded;
        }

        if (!string.IsNullOrEmpty(contentUrl))
            return $"{contentUrl}#{mapping.SchemaTypeName.ToLowerInvariant()}";

        return null;
    }

    /// <summary>
    /// Expands @id template tokens. Supported: {url}, {type}, {key}, {culture},
    /// {siteUrl}. Missing context values expand to empty strings so a template
    /// like "{siteUrl}#{type}" still works when the site URL isn't resolvable
    /// at test time.
    /// </summary>
    internal static string ExpandIdTokens(
        string template,
        string? contentUrl,
        string? siteUrl,
        string schemaTypeName,
        Guid contentKey,
        string? culture)
    {
        return template
            .Replace("{url}", contentUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{type}", schemaTypeName.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{key}", contentKey.ToString("D"), StringComparison.OrdinalIgnoreCase)
            .Replace("{culture}", culture ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{siteUrl}", siteUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Two-axis resolution: first determines WHERE (which node) via SourceType,
    /// then HOW (value extraction) via the resolver factory based on property editor alias.
    /// </summary>
    private object? ResolveValue(PropertyMapping propMapping, IPublishedContent content, string? culture)
    {
        // Complex type creates a nested Thing with sub-property mappings
        if (propMapping.SourceType == "complexType")
            return ResolveComplexType(propMapping, content, culture);

        // Static values bypass resolver entirely
        if (propMapping.SourceType == "static")
            return propMapping.StaticValue;

        // Determine the target node based on SourceType (WHERE axis)
        var targetNode = ResolveTargetNode(propMapping, content);
        if (targetNode is null)
            return null;

        if (string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias))
            return null;

        // Built-in properties (URL, Name, dates) bypass GetProperty() — resolve directly
        if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propMapping.ContentTypePropertyAlias))
        {
            var builtInResolver = _resolverFactory.GetResolver(SchemeWeaverConstants.BuiltInProperties.EditorAlias);
            var builtInContext = new PropertyResolverContext
            {
                Content = targetNode,
                Mapping = propMapping,
                PropertyAlias = propMapping.ContentTypePropertyAlias,
                SchemaTypeRegistry = _registry,
                MappingRepository = _repository,
                HttpContextAccessor = _httpContextAccessor,
                ResolverFactory = _resolverFactory,
                Property = null,
                RecursionDepth = 0,
                MaxRecursionDepth = _options.MaxRecursionDepth,
                Culture = culture
            };
            return builtInResolver.Resolve(builtInContext);
        }

        // Get the property and its editor alias
        var publishedProperty = targetNode.GetProperty(propMapping.ContentTypePropertyAlias);
        if (publishedProperty is null)
            return null;

        var editorAlias = publishedProperty.PropertyType?.EditorAlias;

        // Select resolver based on editor alias (HOW axis)
        var resolver = _resolverFactory.GetResolver(editorAlias);

        var context = new PropertyResolverContext
        {
            Content = targetNode,
            Mapping = propMapping,
            PropertyAlias = propMapping.ContentTypePropertyAlias,
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            ResolverFactory = _resolverFactory,
            Property = publishedProperty,
            RecursionDepth = 0,
            MaxRecursionDepth = _options.MaxRecursionDepth,
            Culture = culture
        };

        return resolver.Resolve(context);
    }

    /// <summary>
    /// Resolves a complex Schema.org type by creating a nested Thing with sub-property mappings.
    /// </summary>
    private object? ResolveComplexType(PropertyMapping propMapping, IPublishedContent content, string? culture)
    {
        var nestedTypeName = propMapping.NestedSchemaTypeName;
        if (string.IsNullOrEmpty(nestedTypeName))
            return null;

        var config = ParseComplexTypeConfig(propMapping.ResolverConfig);
        return ResolveComplexTypeFromConfig(nestedTypeName, config, content, culture);
    }

    /// <summary>
    /// Recursively resolves a complex Schema.org type from its config.
    /// No depth limit — recursion is bounded by the finite JSON structure of resolverConfig.
    /// </summary>
    private object? ResolveComplexTypeFromConfig(
        string typeName, ComplexTypeConfigModel? config, IPublishedContent content, string? culture)
    {
        var clrType = _registry.GetClrType(typeName);
        if (clrType is null || Activator.CreateInstance(clrType) is not Thing nestedInstance)
            return null;

        if (config?.ComplexTypeMappings is null or { Count: 0 })
            return null; // No sub-mappings configured — skip rather than emit empty object

        // Resolve every sub-mapping up front so a resolved media ImageObject can be ADOPTED
        // as the nested instance (see below) before any sub-value is applied to it.
        var resolved = new List<(ComplexTypeMappingEntry SubMapping, object Value)>();
        foreach (var subMapping in config.ComplexTypeMappings.Where(m => !string.IsNullOrEmpty(m.SchemaProperty)))
        {
            object? value = subMapping.SourceType switch
            {
                "static" => subMapping.StaticValue,
                "property" when !string.IsNullOrEmpty(subMapping.ContentTypePropertyAlias) =>
                    ResolveComplexTypePropertyValue(content, subMapping.ContentTypePropertyAlias, culture),
                "complexType" when !string.IsNullOrEmpty(subMapping.ResolverConfig) =>
                    ResolveNestedComplexType(subMapping, content, culture),
                _ => null
            };

            // Apply an optional transform to a property-sourced string sub-value (e.g. stripHtml a
            // RichText sub-property). static stays untransformed, mirroring the top-level static
            // behaviour; complexType yields a Thing, not a string, so the guard skips it. A transform
            // that collapses to whitespace drops the sub-value rather than emitting it blank.
            if (value is string sv
                && string.Equals(subMapping.SourceType, "property", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(subMapping.TransformType))
            {
                var transformed = ApplyTransform(sv, subMapping.TransformType);
                value = string.IsNullOrWhiteSpace(transformed) ? null : transformed;
            }

            if (value is not null)
                resolved.Add((subMapping, value));
        }

        // Render-time repair for persisted MediaPicker→ImageObject shapes: the auto-mapper +
        // enricher historically persisted complexType/ImageObject configs binding e.g.
        // ImageObject.Name <- the media alias. At render time the media resolves (via the
        // resolver factory) to a FULL ImageObject that can never be assigned into a string-only
        // sub-property — it would be silently dropped, leaving an empty {"@type":"ImageObject"}
        // shell. Instead, adopt the first property-sourced resolved ImageObject AS the nested
        // instance, then apply the remaining sub-mappings (e.g. static captions) on top of it.
        if (nestedInstance is ImageObject)
        {
            var adoptIndex = resolved.FindIndex(r =>
                string.Equals(r.SubMapping.SourceType, "property", StringComparison.OrdinalIgnoreCase)
                && FirstImageObject(r.Value) is not null);
            if (adoptIndex >= 0)
            {
                nestedInstance = FirstImageObject(resolved[adoptIndex].Value)!;
                resolved.RemoveAt(adoptIndex);
            }
        }

        foreach (var (subMapping, value) in resolved)
            SchemaPropertySetter.SetPropertyValue(nestedInstance, subMapping.SchemaProperty, value);

        // Empty-shell guard: when no sub-mapping actually landed a value on the nested
        // instance (all resolved null, or every set was dropped by type conversion), omit
        // the nested Thing entirely — {"@type":"Person"} shells are invalid structured data.
        return SchemaPropertySetter.HasResolvedProperty(nestedInstance) ? nestedInstance : null;
    }

    /// <summary>
    /// Extracts the first Schema.NET <see cref="ImageObject"/> from a resolved sub-mapping
    /// value — a single instance or the first of a resolved list (the two shapes
    /// <see cref="Resolvers.MediaPickerResolver"/> produces). Null when the value is not image-shaped.
    /// </summary>
    private static ImageObject? FirstImageObject(object value) => value switch
    {
        ImageObject image => image,
        IEnumerable<IImageObject> many => many.OfType<ImageObject>().FirstOrDefault(),
        _ => null
    };

    /// <summary>
    /// Resolves a nested complex type sub-mapping by parsing its ResolverConfig and recursing.
    /// </summary>
    private object? ResolveNestedComplexType(
        ComplexTypeMappingEntry entry, IPublishedContent content, string? culture)
    {
        var nestedConfig = ParseComplexTypeConfig(entry.ResolverConfig);
        var nestedTypeName = nestedConfig?.SelectedSubType;
        if (string.IsNullOrEmpty(nestedTypeName))
            return null;

        return ResolveComplexTypeFromConfig(nestedTypeName, nestedConfig, content, culture);
    }

    /// <summary>
    /// Resolves a property value for complex type sub-mappings using the resolver factory.
    /// This ensures media pickers, content pickers, etc. are handled correctly.
    /// </summary>
    private object? ResolveComplexTypePropertyValue(IPublishedContent content, string propertyAlias, string? culture)
    {
        var publishedProperty = content.GetProperty(propertyAlias);
        if (publishedProperty is null)
            return null;

        var editorAlias = publishedProperty.PropertyType?.EditorAlias;
        var resolver = _resolverFactory.GetResolver(editorAlias);

        var context = new PropertyResolverContext
        {
            Content = content,
            Mapping = new Models.Entities.PropertyMapping
            {
                ContentTypePropertyAlias = propertyAlias,
                SourceType = "property"
            },
            PropertyAlias = propertyAlias,
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            ResolverFactory = _resolverFactory,
            Property = publishedProperty,
            RecursionDepth = 0,
            MaxRecursionDepth = _options.MaxRecursionDepth,
            Culture = culture
        };

        return resolver.Resolve(context);
    }

    private ComplexTypeConfigModel? ParseComplexTypeConfig(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ComplexTypeConfigModel>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse complex type ResolverConfig JSON: {Json}", json);
            return null;
        }
    }

    /// <summary>
    /// Resolves the target IPublishedContent node based on the SourceType (WHERE axis).
    /// </summary>
    private IPublishedContent? ResolveTargetNode(PropertyMapping propMapping, IPublishedContent content)
    {
        return propMapping.SourceType switch
        {
            "property" or "blockContent" => content,
            "parent" => content.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService),
            "ancestor" => ResolveAncestorNode(content, propMapping),
            "sibling" => ResolveSiblingNode(content, propMapping),
            _ => null
        };
    }

    private IPublishedContent? ResolveAncestorNode(IPublishedContent content, PropertyMapping propMapping)
    {
        var ancestors = content.Ancestors(_navigationQueryService, _publishedStatusFilteringService)
            .Where(node => string.IsNullOrEmpty(propMapping.SourceContentTypeAlias)
                || string.Equals(node.ContentType.Alias, propMapping.SourceContentTypeAlias, StringComparison.OrdinalIgnoreCase));

        foreach (var node in ancestors)
        {
            if (string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias))
                continue;

            // Built-in properties always exist on content nodes
            if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propMapping.ContentTypePropertyAlias))
                return node;

            // Invariant probe: check existence without culture so we find the node
            // regardless of which language variant has a value
            if (node.GetProperty(propMapping.ContentTypePropertyAlias)?.GetValue() is not null)
                return node;
        }

        return null;
    }

    private IPublishedContent? ResolveSiblingNode(IPublishedContent content, PropertyMapping propMapping)
    {
        var parent = content.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
        var siblings = parent?.Children(_navigationQueryService, _publishedStatusFilteringService);
        if (siblings is null)
            return null;

        var candidates = siblings
            .Where(sibling => sibling.Id != content.Id)
            .Where(sibling => string.IsNullOrEmpty(propMapping.SourceContentTypeAlias)
                || string.Equals(sibling.ContentType.Alias, propMapping.SourceContentTypeAlias, StringComparison.OrdinalIgnoreCase));

        foreach (var sibling in candidates)
        {
            if (string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias))
                continue;

            // Built-in properties always exist on content nodes
            if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propMapping.ContentTypePropertyAlias))
                return sibling;

            // Invariant probe: check existence without culture so we find the node
            // regardless of which language variant has a value
            if (sibling.GetProperty(propMapping.ContentTypePropertyAlias)?.GetValue() is not null)
                return sibling;
        }

        return null;
    }

    // Transform logic lives in SchemaValueTransformer so the nested-block resolver applies
    // the same stripHtml/toAbsoluteUrl/formatDate behaviour. This thin wrapper keeps the
    // existing call sites unchanged.
    private string? ApplyTransform(string? value, string? transformType)
        => SchemaValueTransformer.Apply(value, transformType, _httpContextAccessor, _logger);

    /// <summary>
    /// Serialises a Schema.NET Thing to JSON-LD, working around property name collisions
    /// in Schema.NET's interface hierarchy (e.g. IDrug.Funding, IArchiveOrganization.Address).
    /// Uses <see cref="Thing.ToString()"/> for the 697 types that serialise cleanly, and
    /// falls back to a <see cref="DeduplicatingTypeInfoResolver"/> for the ~83 that don't.
    /// </summary>
    private string? SafeSerialize(Thing? thing, int? contentId = null)
    {
        if (thing is null)
            return null;

        try
        {
            return thing.ToString();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("collides with another property"))
        {
            _logger.LogDebug(ex,
                "Schema.NET type {SchemaType} has a property collision — using fallback serialiser",
                thing.GetType().Name);

            try
            {
                return JsonSerializer.Serialize<object>(thing, _deduplicatingOptions);
            }
            catch (JsonException inner)
            {
                _logger.LogWarning(inner,
                    "Fallback serialisation also failed for {SchemaType} (content {ContentId})",
                    thing.GetType().Name, contentId);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to serialise Schema.NET type {SchemaType} for content {ContentId}",
                thing.GetType().Name, contentId);
            return null;
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> GenerateInheritedJsonLdStrings(IPublishedContent content, string? culture = null)
    {
        var inheritedAliases = _repository.GetInheritedMappings()
            .Select(m => m.ContentTypeAlias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (inheritedAliases.Count == 0)
            return [];

        // Walk up from the parent (not the current page) to avoid duplicating the current page's own schema
        var results = new List<string>();
        var current = content.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
        while (current is not null)
        {
            if (inheritedAliases.Contains(current.ContentType.Alias))
            {
                var jsonLd = GenerateJsonLdString(current, culture);
                if (!string.IsNullOrEmpty(jsonLd))
                    results.Add(jsonLd);
            }

            current = current.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
        }

        results.Reverse(); // Root-first order: Website before intermediate schemas
        return results;
    }

    /// <inheritdoc />
    public IEnumerable<string> GenerateBlockElementJsonLdStrings(IPublishedContent content, string? culture = null)
    {
        // Batch-load all mappings AND all property mappings in two queries and
        // index by alias / mapping id for O(1) lookups. This avoids N+1 queries
        // when a page has multiple block element types (or many blocks of the
        // same type).
        var allMappings = _repository.GetAll()
            .Where(m => m.IsEnabled)
            .ToDictionary(m => m.ContentTypeAlias, StringComparer.OrdinalIgnoreCase);
        var propertyMappingsByMappingId = _repository.GetAllPropertyMappingsByMappingId();

        // Identify properties already explicitly mapped via blockContent source type to avoid duplicates
        allMappings.TryGetValue(content.ContentType.Alias, out var currentMapping);
        var explicitBlockProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (currentMapping is not null)
        {
            var currentPropertyMappings = propertyMappingsByMappingId.GetValueOrDefault(currentMapping.Id) ?? [];
            foreach (var pm in currentPropertyMappings
                .Where(pm => pm.SourceType == "blockContent" && !string.IsNullOrEmpty(pm.ContentTypePropertyAlias)))
            {
                explicitBlockProperties.Add(pm.ContentTypePropertyAlias!);
            }
        }

        foreach (var property in content.Properties
            .Where(p => p.PropertyType?.EditorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid")
            .Where(p => !explicitBlockProperties.Contains(p.Alias)))
        {
            var value = property.GetValue(culture: culture);
            if (value is null)
                continue;

            // Walk the whole block subtree: top-level blocks, Block Grid area blocks, AND
            // blocks nested inside a block's own Block List/Grid properties — so a nested
            // block that carries its own mapping still emits a standalone JSON-LD script.
            var visited = new HashSet<Guid>();
            foreach (var element in EnumerateNestedBlockElements(value, culture, 0, visited))
            {
                if (!allMappings.TryGetValue(element.ContentType.Alias, out var mapping))
                    continue;

                var elementPropertyMappings = propertyMappingsByMappingId.GetValueOrDefault(mapping.Id) ?? [];
                var thing = GenerateThingFromElement(element, mapping, elementPropertyMappings);
                if (thing is not null)
                {
                    var jsonLd = SafeSerialize(thing);
                    if (!string.IsNullOrEmpty(jsonLd))
                        yield return jsonLd;
                }
            }
        }
    }

    /// <summary>
    /// Maximum block-nesting depth walked when discovering standalone block-element JSON-LD,
    /// matching <see cref="Resolvers.PropertyResolverContext.MaxRecursionDepth"/>.
    /// </summary>
    private const int MaxBlockDiscoveryDepth = 3;

    /// <summary>
    /// Depth-first enumeration of every block element reachable from a Block List/Grid value:
    /// top-level blocks, Block Grid area blocks, and blocks held in a block element's own
    /// Block List/Grid properties. Depth-capped and de-duplicated by element key to guard
    /// against deep or cyclic structures.
    /// </summary>
    private static IEnumerable<IPublishedElement> EnumerateNestedBlockElements(
        object? value, string? culture, int depth, HashSet<Guid> visited)
    {
        if (depth > MaxBlockDiscoveryDepth)
            yield break;

        IEnumerable<IPublishedElement>? items = value switch
        {
            Umbraco.Cms.Core.Models.Blocks.BlockListModel blockList => blockList.Select(b => b.Content),
            Umbraco.Cms.Core.Models.Blocks.BlockGridModel blockGrid => FlattenGrid(blockGrid),
            _ => null
        };

        if (items is null)
            yield break;

        foreach (var element in items)
        {
            if (!visited.Add(element.Key))
                continue;

            yield return element;

            foreach (var nestedProperty in element.Properties
                .Where(p => p.PropertyType?.EditorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid"))
            {
                var nestedValue = nestedProperty.GetValue(culture: culture);
                foreach (var nested in EnumerateNestedBlockElements(nestedValue, culture, depth + 1, visited))
                    yield return nested;
            }
        }
    }

    /// <summary>Flattens a Block Grid (top-level items plus every nested area) into one sequence.</summary>
    private static IEnumerable<IPublishedElement> FlattenGrid(
        IEnumerable<Umbraco.Cms.Core.Models.Blocks.BlockGridItem> items)
    {
        foreach (var item in items)
        {
            yield return item.Content;

            foreach (var area in item.Areas)
                foreach (var areaItem in FlattenGrid(area))
                    yield return areaItem;
        }
    }

    /// <summary>
    /// Generates a Thing from a block element using its schema mapping.
    /// Only supports "property" and "static" source types (block elements have no parents/ancestors).
    /// </summary>
    private Thing? GenerateThingFromElement(
        IPublishedElement element,
        SchemaMapping mapping,
        IEnumerable<PropertyMapping> propertyMappings)
    {
        var clrType = _registry.GetClrType(mapping.SchemaTypeName);
        if (clrType is null)
            return null;

        if (Activator.CreateInstance(clrType) is not Thing instance)
            return null;

        foreach (var propMapping in propertyMappings)
        {
            try
            {
                object? value = propMapping.SourceType switch
                {
                    "static" => propMapping.StaticValue,
                    "property" when !string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias) =>
                        ResolveElementPropertyValue(element, propMapping.ContentTypePropertyAlias),
                    _ => null
                };

                if (value is null)
                    continue;

                if (value is string stringValue)
                {
                    if (string.IsNullOrWhiteSpace(stringValue))
                        continue;
                    value = ApplyTransform(stringValue, propMapping.TransformType);
                }

                // Guard against null after transform (ApplyTransform can return null)
                if (value is null or string { Length: 0 })
                    continue;

                SchemaPropertySetter.SetPropertyValue(instance, propMapping.SchemaPropertyName, value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to map property {Property} for block element {ElementType}",
                    propMapping.SchemaPropertyName, element.ContentType.Alias);
            }
        }

        return instance;
    }

    /// <summary>
    /// Resolves a property value from an IPublishedElement (block content).
    /// Uses the resolver factory for proper handling of media pickers, rich text, etc.
    /// </summary>
    private object? ResolveElementPropertyValue(IPublishedElement element, string propertyAlias)
    {
        var publishedProperty = element.GetProperty(propertyAlias);
        if (publishedProperty is null)
            return null;

        // Use the utility for media extraction, fall back to GetValue().ToString()
        return SchemaPropertySetter.ResolveElementPropertyValue(element, propertyAlias, _httpContextAccessor);
    }

}
