using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
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
    private readonly ILogger<JsonLdGenerator> _logger;

    public JsonLdGenerator(
        ISchemaMappingRepository repository,
        ISchemaTypeRegistry registry,
        IHttpContextAccessor httpContextAccessor,
        IDocumentNavigationQueryService navigationQueryService,
        IPublishedContentStatusFilteringService publishedStatusFilteringService,
        IPropertyValueResolverFactory resolverFactory,
        ILogger<JsonLdGenerator> logger)
    {
        _repository = repository;
        _registry = registry;
        _httpContextAccessor = httpContextAccessor;
        _navigationQueryService = navigationQueryService;
        _publishedStatusFilteringService = publishedStatusFilteringService;
        _resolverFactory = resolverFactory;
        _logger = logger;
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

                // Apply transforms only to string values
                if (value is string stringValue)
                {
                    var transformed = ApplyTransform(stringValue, propMapping.TransformType);
                    if (transformed is null) continue;
                    value = transformed;
                }

                SchemaPropertySetter.SetPropertyValue(instance, propMapping.SchemaPropertyName, value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to map property {Property} for content {ContentId}",
                    propMapping.SchemaPropertyName, content.Id);
            }
        }

        return instance;
    }

    public string? GenerateJsonLdString(IPublishedContent content)
    {
        return GenerateJsonLd(content)?.ToString();
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
            Property = publishedProperty,
            RecursionDepth = 0
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

        var clrType = _registry.GetClrType(nestedTypeName);
        if (clrType is null || Activator.CreateInstance(clrType) is not Thing nestedInstance)
            return null;

        var config = ParseComplexTypeConfig(propMapping.ResolverConfig);
        if (config?.ComplexTypeMappings is null or { Count: 0 })
            return nestedInstance;

        foreach (var subMapping in config.ComplexTypeMappings)
        {
            if (string.IsNullOrEmpty(subMapping.SchemaProperty))
                continue;

            object? value = subMapping.SourceType switch
            {
                "static" => subMapping.StaticValue,
                "property" when !string.IsNullOrEmpty(subMapping.ContentTypePropertyAlias) =>
                    ResolveComplexTypePropertyValue(content, subMapping.ContentTypePropertyAlias),
                _ => null
            };

            if (value is not null)
                SchemaPropertySetter.SetPropertyValue(nestedInstance, subMapping.SchemaProperty, value);
        }

        return nestedInstance;
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
            Property = publishedProperty,
            RecursionDepth = 0
        };

        return resolver.Resolve(context);
    }

    private static ComplexTypeConfigModel? ParseComplexTypeConfig(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ComplexTypeConfigModel>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
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

            if (!string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias)
                && node.GetProperty(propMapping.ContentTypePropertyAlias)?.GetValue() is not null)
            {
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

            if (!string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias)
                && sibling.GetProperty(propMapping.ContentTypePropertyAlias)?.GetValue() is not null)
            {
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

}
