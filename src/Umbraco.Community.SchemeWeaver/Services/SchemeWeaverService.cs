using System.Text.Json;
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
    private readonly IDataTypeService _dataTypeService;
    private readonly ILogger<SchemeWeaverService> _logger;

    public SchemeWeaverService(
        ISchemaTypeRegistry registry,
        ISchemaAutoMapper autoMapper,
        IJsonLdGenerator generator,
        ISchemaMappingRepository repository,
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        ILogger<SchemeWeaverService> logger)
    {
        _registry = registry;
        _autoMapper = autoMapper;
        _generator = generator;
        _repository = repository;
        _contentTypeService = contentTypeService;
        _dataTypeService = dataTypeService;
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
        entity.IsInherited = dto.IsInherited;

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
            ResolverConfig = p.ResolverConfig,
            DynamicRootConfig = p.DynamicRootConfig
        });

        _repository.SavePropertyMappings(saved.Id, propertyEntities);

        _logger.LogInformation("Saved schema mapping for {Alias} -> {SchemaType}",
            dto.ContentTypeAlias, dto.SchemaTypeName);

        return GetMapping(dto.ContentTypeAlias)
            ?? throw new InvalidOperationException($"Failed to retrieve mapping after save for '{dto.ContentTypeAlias}'.");
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

    public JsonLdPreviewResponse GeneratePreview(IPublishedContent content, string? culture = null)
    {
        var response = new JsonLdPreviewResponse();

        try
        {
            var jsonLd = _generator.GenerateJsonLdString(content, culture);
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

    public JsonLdPreviewResponse GenerateMockPreview(string contentTypeAlias)
    {
        var response = new JsonLdPreviewResponse();
        var mapping = _repository.GetByContentTypeAlias(contentTypeAlias);
        if (mapping is not { IsEnabled: true })
        {
            response.Errors.Add("No mapping found or mapping is disabled.");
            return response;
        }

        var propertyMappings = _repository.GetPropertyMappings(mapping.Id);
        var result = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = mapping.SchemaTypeName,
        };

        foreach (var pm in propertyMappings)
        {
            object? value = pm.SourceType switch
            {
                "static" => pm.StaticValue,
                "blockContent" => $"[BlockList: {pm.ContentTypePropertyAlias} → {pm.NestedSchemaTypeName}]",
                "complexType" => $"[{pm.NestedSchemaTypeName}]",
                _ when SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(pm.ContentTypePropertyAlias) =>
                    GetBuiltInMockValue(pm.ContentTypePropertyAlias),
                _ when !string.IsNullOrEmpty(pm.ContentTypePropertyAlias) => $"[{pm.ContentTypePropertyAlias}]",
                _ => null
            };

            if (value is not null)
                result[pm.SchemaPropertyName] = value;
        }

        response.JsonLd = JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true });
        response.IsValid = true;
        return response;
    }

    private static string GetBuiltInMockValue(string? alias) => alias switch
    {
        SchemeWeaverConstants.BuiltInProperties.Url => "https://example.com/page-url",
        SchemeWeaverConstants.BuiltInProperties.Name => "[Content Name]",
        SchemeWeaverConstants.BuiltInProperties.CreateDate => "2024-01-15T10:30:00+00:00",
        SchemeWeaverConstants.BuiltInProperties.UpdateDate => "2024-03-20T14:45:00+00:00",
        _ => $"[{alias}]"
    };

    public IEnumerable<SchemaTypeInfo> GetSchemaTypes() => _registry.GetAllTypes();

    public IEnumerable<SchemaTypeInfo> SearchSchemaTypes(string query) => _registry.Search(query);

    public IEnumerable<SchemaPropertyInfo> GetSchemaProperties(string typeName) => _registry.GetProperties(typeName);

    public async Task<IEnumerable<BlockElementTypeInfo>> GetBlockElementTypesAsync(string contentTypeAlias, string propertyAlias)
    {
        var contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
            return [];

        var property = contentType.PropertyTypes.FirstOrDefault(
            p => string.Equals(p.Alias, propertyAlias, StringComparison.OrdinalIgnoreCase));
        if (property is null)
            return [];

        if (!SchemeWeaverConstants.PropertyEditors.BlockEditorAliases.Contains(property.PropertyEditorAlias))
            return [];

        var dataType = await _dataTypeService.GetAsync(property.DataTypeKey).ConfigureAwait(false);
        if (dataType is null)
            return [];

        var elementTypeKeys = ExtractBlockElementTypeKeys(dataType);
        if (elementTypeKeys.Count == 0)
            return [];

        return elementTypeKeys
            .Select(key => _contentTypeService.Get(key))
            .Where(elementType => elementType is not null)
            .Select(elementType => new BlockElementTypeInfo
            {
                Alias = elementType!.Alias,
                Name = elementType.Name ?? elementType.Alias,
                Properties = elementType.PropertyTypes.Select(p => p.Alias).ToList()
            })
            .ToList();
    }

    /// <summary>
    /// Extracts content element type keys from a BlockList or BlockGrid data type configuration.
    /// </summary>
    private static List<Guid> ExtractBlockElementTypeKeys(Umbraco.Cms.Core.Models.IDataType dataType)
    {
        var keys = new List<Guid>();

        if (dataType.ConfigurationData is null)
            return keys;

        // BlockList/BlockGrid stores blocks configuration as JSON
        if (!dataType.ConfigurationData.TryGetValue("blocks", out var blocksValue))
            return keys;

        try
        {
            var blocksJson = blocksValue?.ToString();
            if (string.IsNullOrEmpty(blocksJson))
                return keys;

            using var doc = JsonDocument.Parse(blocksJson);
            foreach (var block in doc.RootElement.EnumerateArray())
            {
                if (block.TryGetProperty("contentElementTypeKey", out var keyProp) &&
                    Guid.TryParse(keyProp.GetString(), out var elementKey))
                {
                    keys.Add(elementKey);
                }
            }
        }
        catch (JsonException)
        {
            // Configuration format not as expected — return empty
        }

        return keys;
    }

    private static SchemaMappingDto ToDto(SchemaMapping mapping, IEnumerable<PropertyMapping> propertyMappings)
        => new()
        {
            ContentTypeAlias = mapping.ContentTypeAlias,
            ContentTypeKey = mapping.ContentTypeKey,
            SchemaTypeName = mapping.SchemaTypeName,
            IsEnabled = mapping.IsEnabled,
            IsInherited = mapping.IsInherited,
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
                ResolverConfig = p.ResolverConfig,
                DynamicRootConfig = p.DynamicRootConfig
            }).ToList()
        };
}
