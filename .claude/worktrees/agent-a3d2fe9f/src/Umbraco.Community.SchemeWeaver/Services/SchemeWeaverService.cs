using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Orchestrator service combining registry, auto-mapper, generator, and repository.
/// </summary>
public class SchemeWeaverService : ISchemeWeaverService
{
    private readonly ISchemaTypeRegistry _registry;
    private readonly ISchemaAutoMapper _autoMapper;
    private readonly IJsonLdGenerator _generator;
    private readonly ISchemaMappingRepository _repository;
    private readonly IContentTypeService _contentTypeService;
    private readonly ILogger<SchemeWeaverService> _logger;

    public SchemeWeaverService(
        ISchemaTypeRegistry registry,
        ISchemaAutoMapper autoMapper,
        IJsonLdGenerator generator,
        ISchemaMappingRepository repository,
        IContentTypeService contentTypeService,
        ILogger<SchemeWeaverService> logger)
    {
        _registry = registry;
        _autoMapper = autoMapper;
        _generator = generator;
        _repository = repository;
        _contentTypeService = contentTypeService;
        _logger = logger;
    }

    public SchemaMappingDto? GetMapping(string contentTypeAlias)
    {
        var mapping = _repository.GetByContentTypeAlias(contentTypeAlias);
        if (mapping is null) return null;

        var propertyMappings = _repository.GetPropertyMappings(mapping.Id);
        return ToDto(mapping, propertyMappings);
    }

    public IEnumerable<SchemaMappingDto> GetAllMappings()
    {
        var mappings = _repository.GetAll();
        return mappings.Select(m =>
        {
            var propertyMappings = _repository.GetPropertyMappings(m.Id);
            return ToDto(m, propertyMappings);
        });
    }

    public SchemaMappingDto SaveMapping(SchemaMappingDto dto)
    {
        var existing = _repository.GetByContentTypeAlias(dto.ContentTypeAlias);

        var entity = existing ?? new SchemaMapping();
        entity.ContentTypeAlias = dto.ContentTypeAlias;
        entity.ContentTypeKey = dto.ContentTypeKey;

        if (entity.ContentTypeKey == Guid.Empty && !string.IsNullOrEmpty(dto.ContentTypeAlias))
        {
            var contentType = _contentTypeService.Get(dto.ContentTypeAlias);
            if (contentType != null)
                entity.ContentTypeKey = contentType.Key;
        }

        entity.SchemaTypeName = dto.SchemaTypeName;
        entity.IsEnabled = dto.IsEnabled;

        var saved = _repository.Save(entity);

        var propertyEntities = dto.PropertyMappings.Select(p => new PropertyMapping
        {
            SchemaMappingId = saved.Id,
            SchemaPropertyName = p.SchemaPropertyName,
            SourceType = p.SourceType,
            ContentTypePropertyAlias = p.ContentTypePropertyAlias,
            SourceContentTypeAlias = p.SourceContentTypeAlias,
            TransformType = p.TransformType,
            IsAutoMapped = p.IsAutoMapped,
            StaticValue = p.StaticValue,
            NestedSchemaTypeName = p.NestedSchemaTypeName,
            ResolverConfig = p.ResolverConfig
        });

        _repository.SavePropertyMappings(saved.Id, propertyEntities);

        _logger.LogInformation("Saved schema mapping for {Alias} -> {SchemaType}",
            dto.ContentTypeAlias, dto.SchemaTypeName);

        return GetMapping(dto.ContentTypeAlias)!;
    }

    public void DeleteMapping(string contentTypeAlias)
    {
        var mapping = _repository.GetByContentTypeAlias(contentTypeAlias);
        if (mapping is not null)
        {
            _repository.Delete(mapping.Id);
        }
    }

    public IEnumerable<PropertyMappingSuggestion> AutoMap(string contentTypeAlias, string schemaTypeName)
        => _autoMapper.SuggestMappings(contentTypeAlias, schemaTypeName);

    public JsonLdPreviewResponse GeneratePreview(IPublishedContent content)
    {
        var response = new JsonLdPreviewResponse();

        try
        {
            var jsonLd = _generator.GenerateJsonLdString(content);
            if (jsonLd is not null)
            {
                response.JsonLd = jsonLd;
                response.IsValid = true;
            }
            else
            {
                response.Errors.Add("No schema mapping found or mapping is disabled for this content type.");
            }
        }
        catch (Exception ex)
        {
            response.Errors.Add(ex.Message);
            _logger.LogError(ex, "Error generating JSON-LD preview for content {ContentId}", content.Id);
        }

        return response;
    }

    public IEnumerable<SchemaTypeInfo> GetSchemaTypes() => _registry.GetAllTypes();

    public IEnumerable<SchemaTypeInfo> SearchSchemaTypes(string query) => _registry.Search(query);

    public IEnumerable<SchemaPropertyInfo> GetSchemaProperties(string typeName) => _registry.GetProperties(typeName);

    private static SchemaMappingDto ToDto(SchemaMapping mapping, IEnumerable<PropertyMapping> propertyMappings)
        => new()
        {
            ContentTypeAlias = mapping.ContentTypeAlias,
            ContentTypeKey = mapping.ContentTypeKey,
            SchemaTypeName = mapping.SchemaTypeName,
            IsEnabled = mapping.IsEnabled,
            PropertyMappings = propertyMappings.Select(p => new PropertyMappingDto
            {
                SchemaPropertyName = p.SchemaPropertyName,
                SourceType = p.SourceType,
                ContentTypePropertyAlias = p.ContentTypePropertyAlias,
                SourceContentTypeAlias = p.SourceContentTypeAlias,
                TransformType = p.TransformType,
                IsAutoMapped = p.IsAutoMapped,
                StaticValue = p.StaticValue,
                NestedSchemaTypeName = p.NestedSchemaTypeName,
                ResolverConfig = p.ResolverConfig
            }).ToList()
        };
}
