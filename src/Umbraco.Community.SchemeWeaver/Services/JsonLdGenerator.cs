using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Generates JSON-LD from published content using stored schema mappings.
/// </summary>
public partial class JsonLdGenerator : IJsonLdGenerator
{
    private readonly ISchemaMappingRepository _repository;
    private readonly ISchemaTypeRegistry _registry;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDocumentNavigationQueryService _navigationQueryService;
    private readonly IPublishedContentStatusFilteringService _publishedStatusFilteringService;
    private readonly ILogger<JsonLdGenerator> _logger;

    public JsonLdGenerator(
        ISchemaMappingRepository repository,
        ISchemaTypeRegistry registry,
        IHttpContextAccessor httpContextAccessor,
        IDocumentNavigationQueryService navigationQueryService,
        IPublishedContentStatusFilteringService publishedStatusFilteringService,
        ILogger<JsonLdGenerator> logger)
    {
        _repository = repository;
        _registry = registry;
        _httpContextAccessor = httpContextAccessor;
        _navigationQueryService = navigationQueryService;
        _publishedStatusFilteringService = publishedStatusFilteringService;
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

                value = ApplyTransform(value, propMapping.TransformType);
                if (value is not null)
                {
                    SetPropertyValue(instance, propMapping.SchemaPropertyName, value);
                }
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

    private string? ResolveValue(PropertyMapping propMapping, IPublishedContent content)
    {
        var parent = content.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);

        return propMapping.SourceType switch
        {
            "static" => propMapping.StaticValue,
            "property" => GetPropertyValue(content, propMapping.ContentTypePropertyAlias),
            "parent" => parent is not null
                ? GetPropertyValue(parent, propMapping.ContentTypePropertyAlias)
                : null,
            "ancestor" => ResolveAncestorValue(content, propMapping),
            "sibling" => ResolveSiblingValue(content, propMapping),
            _ => null
        };
    }

    private string? ResolveAncestorValue(IPublishedContent content, PropertyMapping propMapping)
    {
        var ancestors = content.Ancestors(_navigationQueryService, _publishedStatusFilteringService);

        foreach (var node in ancestors)
        {
            if (!string.IsNullOrEmpty(propMapping.SourceContentTypeAlias)
                && !string.Equals(node.ContentType.Alias, propMapping.SourceContentTypeAlias, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = GetPropertyValue(node, propMapping.ContentTypePropertyAlias);
            if (value is not null)
                return value;
        }

        return null;
    }

    private string? ResolveSiblingValue(IPublishedContent content, PropertyMapping propMapping)
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

            var value = GetPropertyValue(sibling, propMapping.ContentTypePropertyAlias);
            if (value is not null)
                return value;
        }

        return null;
    }

    private static string? GetPropertyValue(IPublishedContent node, string? propertyAlias)
    {
        if (string.IsNullOrEmpty(propertyAlias))
            return null;

        return node.GetProperty(propertyAlias)?.GetValue()?.ToString();
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

    private void SetPropertyValue(Thing instance, string propertyName, string value)
    {
        var property = instance.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is not { CanWrite: true })
            return;

        var targetType = property.PropertyType;

        // Try to find an implicit conversion operator that accepts our value type
        var converted = TryConvertViaImplicit(targetType, value)
                     ?? TryConvertViaImplicit(targetType, (object)value);

        if (converted is not null)
        {
            property.SetValue(instance, converted);
            return;
        }

        // Handle OneOrMany<T> types from Schema.NET by building from inside out
        if (targetType is { IsGenericType: true } && targetType.GetGenericTypeDefinition().Name.StartsWith("OneOrMany"))
        {
            var innerType = targetType.GetGenericArguments()[0];

            if (innerType is { IsGenericType: true } && innerType.GetGenericTypeDefinition().Name.StartsWith("Values"))
            {
                var valuesArgs = innerType.GetGenericArguments();

                // Build Values<> via implicit operator
                object? valuesInstance = null;
                if (valuesArgs.Any(t => t == typeof(string)))
                {
                    valuesInstance = TryConvertViaImplicit(innerType, value);
                }

                if (valuesInstance is null && valuesArgs.Any(t => t == typeof(Uri))
                    && Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri))
                {
                    valuesInstance = TryConvertViaImplicit(innerType, uri);
                }

                if (valuesInstance is not null)
                {
                    // Build OneOrMany<> from Values<>
                    var oneOrMany = TryConvertViaImplicit(targetType, valuesInstance);
                    if (oneOrMany is not null)
                    {
                        property.SetValue(instance, oneOrMany);
                        return;
                    }

                    // Try constructor
                    try
                    {
                        var oneOrManyInstance = Activator.CreateInstance(targetType, valuesInstance);
                        property.SetValue(instance, oneOrManyInstance);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to construct OneOrMany for property {Property}", propertyName);
                    }
                }
            }
        }

        // Simple string assignment
        if (targetType == typeof(string))
        {
            property.SetValue(instance, value);
        }
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
