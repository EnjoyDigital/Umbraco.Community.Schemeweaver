using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;
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
    private readonly ILogger<JsonLdGenerator> _logger;
    private readonly SchemeWeaverOptions _options;

    public JsonLdGenerator(
        ISchemaMappingRepository repository,
        ISchemaTypeRegistry registry,
        IHttpContextAccessor httpContextAccessor,
        IDocumentNavigationQueryService navigationQueryService,
        IPublishedContentStatusFilteringService publishedStatusFilteringService,
        IPropertyValueResolverFactory resolverFactory,
        IPublishedUrlProvider urlProvider,
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
        _logger = logger;
        _options = options.Value;
    }

    public Thing? GenerateJsonLd(IPublishedContent content)
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

        foreach (var propMapping in propertyMappings)
        {
            try
            {
                var value = ResolveValue(propMapping, content);
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

                SchemaPropertySetter.SetPropertyValue(instance, propMapping.SchemaPropertyName, value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to map property {Property} for content {ContentId}",
                    propMapping.SchemaPropertyName, content.Id);
            }
        }

        // Set @id from content URL for AI agent discoverability
        var contentUrl = ResolveAbsoluteUrl(content);
        if (!string.IsNullOrEmpty(contentUrl))
        {
            instance.Id = new Uri(contentUrl, UriKind.Absolute);
        }

        return instance;
    }

    public string? GenerateJsonLdString(IPublishedContent content)
    {
        return GenerateJsonLd(content)?.ToString();
    }

    public string? GenerateBreadcrumbJsonLd(IPublishedContent content)
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

            if (!string.IsNullOrEmpty(url))
            {
                listItem.Url = new Uri(url, UriKind.Absolute);
                listItem.Id = new Uri(url, UriKind.Absolute);
            }

            items.Add(listItem);
        }

        breadcrumb.ItemListElement = items;
        return breadcrumb.ToString();
    }

    /// <summary>
    /// Resolves an absolute URL for content using the URL provider with a request-context fallback.
    /// </summary>
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

    /// <summary>
    /// Two-axis resolution: first determines WHERE (which node) via SourceType,
    /// then HOW (value extraction) via the resolver factory based on property editor alias.
    /// </summary>
    private object? ResolveValue(PropertyMapping propMapping, IPublishedContent content)
    {
        // Complex type creates a nested Thing with sub-property mappings
        if (propMapping.SourceType == "complexType")
            return ResolveComplexType(propMapping, content);

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
                MaxRecursionDepth = _options.MaxRecursionDepth
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
            MaxRecursionDepth = _options.MaxRecursionDepth
        };

        return resolver.Resolve(context);
    }

    /// <summary>
    /// Resolves a complex Schema.org type by creating a nested Thing with sub-property mappings.
    /// </summary>
    private object? ResolveComplexType(PropertyMapping propMapping, IPublishedContent content)
    {
        var nestedTypeName = propMapping.NestedSchemaTypeName;
        if (string.IsNullOrEmpty(nestedTypeName))
            return null;

        var config = ParseComplexTypeConfig(propMapping.ResolverConfig);
        return ResolveComplexTypeFromConfig(nestedTypeName, config, content);
    }

    /// <summary>
    /// Recursively resolves a complex Schema.org type from its config.
    /// No depth limit — recursion is bounded by the finite JSON structure of resolverConfig.
    /// </summary>
    private object? ResolveComplexTypeFromConfig(
        string typeName, ComplexTypeConfigModel? config, IPublishedContent content)
    {
        var clrType = _registry.GetClrType(typeName);
        if (clrType is null || Activator.CreateInstance(clrType) is not Thing nestedInstance)
            return null;

        if (config?.ComplexTypeMappings is null or { Count: 0 })
            return null; // No sub-mappings configured — skip rather than emit empty object

        foreach (var subMapping in config.ComplexTypeMappings)
        {
            if (string.IsNullOrEmpty(subMapping.SchemaProperty))
                continue;

            object? value = subMapping.SourceType switch
            {
                "static" => subMapping.StaticValue,
                "property" when !string.IsNullOrEmpty(subMapping.ContentTypePropertyAlias) =>
                    ResolveComplexTypePropertyValue(content, subMapping.ContentTypePropertyAlias),
                "complexType" when !string.IsNullOrEmpty(subMapping.ResolverConfig) =>
                    ResolveNestedComplexType(subMapping, content),
                _ => null
            };

            if (value is not null)
                SchemaPropertySetter.SetPropertyValue(nestedInstance, subMapping.SchemaProperty, value);
        }

        return nestedInstance;
    }

    /// <summary>
    /// Resolves a nested complex type sub-mapping by parsing its ResolverConfig and recursing.
    /// </summary>
    private object? ResolveNestedComplexType(
        ComplexTypeMappingEntry entry, IPublishedContent content)
    {
        var nestedConfig = ParseComplexTypeConfig(entry.ResolverConfig);
        var nestedTypeName = nestedConfig?.SelectedSubType;
        if (string.IsNullOrEmpty(nestedTypeName))
            return null;

        return ResolveComplexTypeFromConfig(nestedTypeName, nestedConfig, content);
    }

    /// <summary>
    /// Resolves a property value for complex type sub-mappings using the resolver factory.
    /// This ensures media pickers, content pickers, etc. are handled correctly.
    /// </summary>
    private object? ResolveComplexTypePropertyValue(IPublishedContent content, string propertyAlias)
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
            MaxRecursionDepth = _options.MaxRecursionDepth
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
        var ancestors = content.Ancestors(_navigationQueryService, _publishedStatusFilteringService);

        foreach (var node in ancestors)
        {
            if (!string.IsNullOrEmpty(propMapping.SourceContentTypeAlias)
                && !string.Equals(node.ContentType.Alias, propMapping.SourceContentTypeAlias, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias))
            {
                // Built-in properties always exist on content nodes
                if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propMapping.ContentTypePropertyAlias))
                    return node;

                if (node.GetProperty(propMapping.ContentTypePropertyAlias)?.GetValue() is not null)
                    return node;
            }
        }

        return null;
    }

    private IPublishedContent? ResolveSiblingNode(IPublishedContent content, PropertyMapping propMapping)
    {
        var parent = content.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
        var siblings = parent?.Children(_navigationQueryService, _publishedStatusFilteringService);
        if (siblings is null)
            return null;

        foreach (var sibling in siblings)
        {
            if (sibling.Id == content.Id)
                continue;

            if (!string.IsNullOrEmpty(propMapping.SourceContentTypeAlias)
                && !string.Equals(sibling.ContentType.Alias, propMapping.SourceContentTypeAlias, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias))
            {
                // Built-in properties always exist on content nodes
                if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propMapping.ContentTypePropertyAlias))
                    return sibling;

                if (sibling.GetProperty(propMapping.ContentTypePropertyAlias)?.GetValue() is not null)
                    return sibling;
            }
        }

        return null;
    }

    private string? ApplyTransform(string? value, string? transformType)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(transformType))
            return value;

        return transformType switch
        {
            "stripHtml" => StripHtmlTags(value),
            "toAbsoluteUrl" => ToAbsoluteUrl(value),
            "formatDate" => DateTime.TryParse(value, out var dt) ? dt.ToString("yyyy-MM-dd") : value,
            _ => value
        };
    }

    /// <summary>
    /// Resolves a relative URL to an absolute URL using the current request's base URL.
    /// </summary>
    private string ToAbsoluteUrl(string value)
    {
        if (!value.StartsWith('/'))
            return value;

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            _logger.LogWarning("Cannot resolve absolute URL: no HttpContext available");
            return value;
        }

        return $"{request.Scheme}://{request.Host}{value}";
    }

    private static string StripHtmlTags(string html)
    {
        return StripHtmlRegex().Replace(html, string.Empty).Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex StripHtmlRegex();

    /// <inheritdoc />
    public IEnumerable<string> GenerateInheritedJsonLdStrings(IPublishedContent content)
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
                var jsonLd = GenerateJsonLdString(current);
                if (!string.IsNullOrEmpty(jsonLd))
                    results.Add(jsonLd);
            }

            current = current.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
        }

        results.Reverse(); // Root-first order: Website before intermediate schemas
        return results;
    }

    /// <inheritdoc />
    public IEnumerable<string> GenerateBlockElementJsonLdStrings(IPublishedContent content)
    {
        // Batch-load all mappings in one query and index by alias for O(1) lookups.
        // This avoids N+1 queries when a page has multiple block element types.
        var allMappings = _repository.GetAll()
            .Where(m => m.IsEnabled)
            .ToDictionary(m => m.ContentTypeAlias, StringComparer.OrdinalIgnoreCase);

        // Identify properties already explicitly mapped via blockContent source type to avoid duplicates
        allMappings.TryGetValue(content.ContentType.Alias, out var currentMapping);
        var explicitBlockProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (currentMapping is not null)
        {
            var currentPropertyMappings = _repository.GetPropertyMappings(currentMapping.Id);
            foreach (var pm in currentPropertyMappings)
            {
                if (pm.SourceType == "blockContent" && !string.IsNullOrEmpty(pm.ContentTypePropertyAlias))
                    explicitBlockProperties.Add(pm.ContentTypePropertyAlias);
            }
        }

        foreach (var property in content.Properties)
        {
            var editorAlias = property.PropertyType?.EditorAlias;
            if (editorAlias is not ("Umbraco.BlockList" or "Umbraco.BlockGrid"))
                continue;

            // Skip properties already explicitly mapped as blockContent
            if (explicitBlockProperties.Contains(property.Alias))
                continue;

            var value = property.GetValue();
            if (value is null)
                continue;

            IEnumerable<IPublishedElement>? blockElements = value switch
            {
                Umbraco.Cms.Core.Models.Blocks.BlockListModel blockList => blockList.Select(b => b.Content),
                Umbraco.Cms.Core.Models.Blocks.BlockGridModel blockGrid => blockGrid.Select(b => b.Content),
                _ => null
            };

            if (blockElements is null)
                continue;

            foreach (var element in blockElements)
            {
                if (!allMappings.TryGetValue(element.ContentType.Alias, out var mapping))
                    continue;

                var thing = GenerateThingFromElement(element, mapping);
                if (thing is not null)
                {
                    var jsonLd = thing.ToString();
                    if (!string.IsNullOrEmpty(jsonLd))
                        yield return jsonLd;
                }
            }
        }
    }

    /// <summary>
    /// Generates a Thing from a block element using its schema mapping.
    /// Only supports "property" and "static" source types (block elements have no parents/ancestors).
    /// </summary>
    private Thing? GenerateThingFromElement(IPublishedElement element, SchemaMapping mapping)
    {
        var clrType = _registry.GetClrType(mapping.SchemaTypeName);
        if (clrType is null)
            return null;

        if (Activator.CreateInstance(clrType) is not Thing instance)
            return null;

        var propertyMappings = _repository.GetPropertyMappings(mapping.Id);

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
