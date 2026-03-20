using System.Collections;
using System.Reflection;
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

                SetPropertyValue(instance, propMapping.SchemaPropertyName, value);
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
    /// Resolves the target IPublishedContent node based on the SourceType (WHERE axis).
    /// </summary>
    private IPublishedContent? ResolveTargetNode(PropertyMapping propMapping, IPublishedContent content)
    {
        return propMapping.SourceType switch
        {
            "property" => content,
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

    /// <summary>
    /// Sets a property value on a Schema.NET Thing instance.
    /// Accepts string, Uri, Thing, or IEnumerable&lt;Thing&gt; values.
    /// </summary>
    private void SetPropertyValue(Thing instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is not { CanWrite: true })
            return;

        var targetType = property.PropertyType;

        // If the value is already the correct type, set directly
        if (targetType.IsInstanceOfType(value))
        {
            property.SetValue(instance, value);
            return;
        }

        // Try to find an implicit conversion operator that accepts our value type
        var converted = TryConvertViaImplicit(targetType, value);

        if (converted is not null)
        {
            property.SetValue(instance, converted);
            return;
        }

        // Handle IEnumerable<Thing> for collection properties (e.g., block content results)
        if (value is IEnumerable<Thing> things)
        {
            if (TrySetCollectionValue(property, instance, targetType, things))
                return;
        }

        // Handle OneOrMany<T> types from Schema.NET by building from inside out
        if (targetType is { IsGenericType: true } && targetType.GetGenericTypeDefinition().Name.StartsWith("OneOrMany"))
        {
            if (TrySetOneOrManyValue(property, instance, targetType, value))
                return;
        }

        // Simple string assignment
        if (targetType == typeof(string) && value is string strVal)
        {
            property.SetValue(instance, strVal);
        }
    }

    /// <summary>
    /// Attempts to set a collection of Thing instances on a OneOrMany property.
    /// </summary>
    private bool TrySetCollectionValue(PropertyInfo property, Thing instance, Type targetType, IEnumerable<Thing> things)
    {
        var thingList = things.ToList();
        if (thingList.Count == 0)
            return false;

        // Try to convert each item via implicit and build the collection
        if (targetType is { IsGenericType: true } && targetType.GetGenericTypeDefinition().Name.StartsWith("OneOrMany"))
        {
            var innerType = targetType.GetGenericArguments()[0];

            // Try converting the first item to get the Values<> type right
            var firstConverted = TryConvertViaImplicit(innerType, thingList[0]);
            if (firstConverted is not null)
            {
                // Create a List<innerType> and add all converted items
                var listType = typeof(List<>).MakeGenericType(innerType);
                var list = (IList)Activator.CreateInstance(listType)!;
                list.Add(firstConverted);

                for (var i = 1; i < thingList.Count; i++)
                {
                    var itemConverted = TryConvertViaImplicit(innerType, thingList[i]);
                    if (itemConverted is not null)
                        list.Add(itemConverted);
                }

                // Try constructing OneOrMany from IEnumerable<innerType>
                try
                {
                    var oneOrManyInstance = Activator.CreateInstance(targetType, list);
                    property.SetValue(instance, oneOrManyInstance);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to construct OneOrMany collection for property {Property}", property.Name);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to set a single value on a OneOrMany property by building from inside out.
    /// </summary>
    private bool TrySetOneOrManyValue(PropertyInfo property, Thing instance, Type targetType, object value)
    {
        var innerType = targetType.GetGenericArguments()[0];

        if (innerType is { IsGenericType: true } && innerType.GetGenericTypeDefinition().Name.StartsWith("Values"))
        {
            var valuesArgs = innerType.GetGenericArguments();

            // Build Values<> via implicit operator
            object? valuesInstance = null;

            // Try direct conversion from the value
            valuesInstance = TryConvertViaImplicit(innerType, value);

            // If value is a string, try string-specific conversions
            if (valuesInstance is null && value is string stringValue)
            {
                if (valuesArgs.Any(t => t == typeof(string)))
                {
                    valuesInstance = TryConvertViaImplicit(innerType, stringValue);
                }

                if (valuesInstance is null && valuesArgs.Any(t => t == typeof(Uri))
                    && Uri.TryCreate(stringValue, UriKind.RelativeOrAbsolute, out var uri))
                {
                    valuesInstance = TryConvertViaImplicit(innerType, uri);
                }
            }

            if (valuesInstance is not null)
            {
                // Build OneOrMany<> from Values<>
                var oneOrMany = TryConvertViaImplicit(targetType, valuesInstance);
                if (oneOrMany is not null)
                {
                    property.SetValue(instance, oneOrMany);
                    return true;
                }

                // Try constructor
                try
                {
                    var oneOrManyInstance = Activator.CreateInstance(targetType, valuesInstance);
                    property.SetValue(instance, oneOrManyInstance);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to construct OneOrMany for property {Property}", property.Name);
                }
            }
        }

        return false;
    }

    private object? TryConvertViaImplicit(Type targetType, object value)
    {
        // Search for op_Implicit on the target type
        var methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Implicit" && m.GetParameters().Length == 1);

        foreach (var method in methods)
        {
            var paramType = method.GetParameters()[0].ParameterType;
            if (!paramType.IsAssignableFrom(value.GetType()))
                continue;

            try
            {
                return method.Invoke(null, [value]);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Implicit conversion failed on target type {TargetType}", targetType.Name);
            }
        }

        // Also search on the source type (value's type) for op_Implicit returning targetType
        var sourceMethods = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Implicit" && m.ReturnType == targetType && m.GetParameters().Length == 1);

        foreach (var method in sourceMethods)
        {
            var paramType = method.GetParameters()[0].ParameterType;
            if (!paramType.IsAssignableFrom(value.GetType()))
                continue;

            try
            {
                return method.Invoke(null, [value]);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Implicit conversion failed on source type {SourceType}", value.GetType().Name);
            }
        }

        return null;
    }
}
