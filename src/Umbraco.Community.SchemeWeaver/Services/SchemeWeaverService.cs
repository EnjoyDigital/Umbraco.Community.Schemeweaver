using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services.Validation;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Orchestrator service combining registry, auto-mapper, generator, and repository.
/// </summary>
public class SchemeWeaverService : ISchemeWeaverService
{
    private readonly ISchemaTypeRegistry _registry;
    private readonly ISchemaAutoMapper _autoMapper;
    private readonly IJsonLdGenerator _generator;
    private readonly IGraphGenerator _graphGenerator;
    private readonly ISchemaMappingRepository _repository;
    private readonly IContentTypeService _contentTypeService;
    private readonly IDataTypeService _dataTypeService;
    private readonly ISchemaValidator _validator;
    private readonly IBlockSchemaSuggester _blockSchemaSuggester;
    private readonly ISchemaRangeValidator _rangeValidator;
    private readonly IMappingReachabilityClassifier _reachabilityClassifier;
    private readonly IMappingDriftReporter _driftReporter;
    private readonly IEventAggregator _eventAggregator;
    private readonly SchemeWeaverOptions _options;
    private readonly ILogger<SchemeWeaverService> _logger;

    public SchemeWeaverService(
        ISchemaTypeRegistry registry,
        ISchemaAutoMapper autoMapper,
        IJsonLdGenerator generator,
        IGraphGenerator graphGenerator,
        ISchemaMappingRepository repository,
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        ISchemaValidator validator,
        IBlockSchemaSuggester blockSchemaSuggester,
        ISchemaRangeValidator rangeValidator,
        IMappingReachabilityClassifier reachabilityClassifier,
        IMappingDriftReporter driftReporter,
        IEventAggregator eventAggregator,
        IOptions<SchemeWeaverOptions> options,
        ILogger<SchemeWeaverService> logger)
    {
        _registry = registry;
        _autoMapper = autoMapper;
        _generator = generator;
        _graphGenerator = graphGenerator;
        _repository = repository;
        _contentTypeService = contentTypeService;
        _dataTypeService = dataTypeService;
        _validator = validator;
        _blockSchemaSuggester = blockSchemaSuggester;
        _rangeValidator = rangeValidator;
        _reachabilityClassifier = reachabilityClassifier;
        _driftReporter = driftReporter;
        _eventAggregator = eventAggregator;
        _options = options.Value;
        _logger = logger;
    }

    public SchemaMappingDto? GetMapping(string contentTypeAlias)
    {
        var mapping = _repository.GetByContentTypeAlias(contentTypeAlias);
        if (mapping is null) return null;

        var propertyMappings = _repository.GetPropertyMappings(mapping.Id);
        var dto = ToDto(mapping, propertyMappings);

        // Single read: enrich with reachability (cheap), structural range
        // warnings (a registry walk per property mapping) and disk/DB drift.
        dto.Reachability = _reachabilityClassifier.Classify(dto.ContentTypeAlias);
        dto.Warnings = BuildWarningDtos(dto);
        dto.DriftStatus = _driftReporter.GetStatus(dto.ContentTypeAlias);
        return dto;
    }

    public IEnumerable<SchemaMappingDto> GetAllMappings()
    {
        // Two queries total (mappings + all property mappings) instead of 1 + N.
        // Grouping is done in memory so a site with many mappings doesn't issue
        // one DB round-trip per mapping.
        var mappings = _repository.GetAll();
        var propertyMappingsByMappingId = _repository.GetAllPropertyMappingsByMappingId();

        // One drift report for the whole list (the reporter reads each .config once) instead
        // of a file probe per mapping. Keyed by alias for O(1) lookup below.
        var driftByAlias = _driftReporter.GetReport().Items
            .ToDictionary(i => i.ContentTypeAlias, i => i.Status, StringComparer.OrdinalIgnoreCase);

        return mappings.Select(m =>
        {
            var dto = ToDto(m, propertyMappingsByMappingId.GetValueOrDefault(m.Id) ?? []);
            // List view: reachability + drift only. The range validator is bounded out
            // here to keep a many-mapping listing cheap.
            dto.Reachability = _reachabilityClassifier.Classify(dto.ContentTypeAlias);
            dto.DriftStatus = driftByAlias.GetValueOrDefault(dto.ContentTypeAlias)
                ?? _driftReporter.GetStatus(dto.ContentTypeAlias);
            return dto;
        });
    }

    /// <summary>
    /// Runs the structural range validator over a DTO and maps each finding to a
    /// camelCase <see cref="ValidationIssueDto"/> with severity <c>warning</c>.
    /// </summary>
    private List<ValidationIssueDto> BuildWarningDtos(SchemaMappingDto dto)
        => _rangeValidator.Validate(dto)
            .Select(i => new ValidationIssueDto("warning", i.SchemaType, i.Path, i.Message))
            .ToList();

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
        entity.IdOverride = string.IsNullOrWhiteSpace(dto.IdOverride) ? null : dto.IdOverride.Trim();

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
            DynamicRootConfig = p.DynamicRootConfig,
            TargetPieceKey = string.IsNullOrWhiteSpace(p.TargetPieceKey) ? null : p.TargetPieceKey.Trim()
        });

        _repository.SavePropertyMappings(saved.Id, propertyEntities);

        _logger.LogInformation("Saved schema mapping for {Alias} -> {SchemaType}",
            dto.ContentTypeAlias, dto.SchemaTypeName);

        // Re-fetch so the returned DTO carries the persisted property mappings
        // plus Reachability + Warnings enrichment (done inside GetMapping).
        var result = GetMapping(dto.ContentTypeAlias)
            ?? throw new InvalidOperationException($"Failed to retrieve mapping after save for '{dto.ContentTypeAlias}'.");

        // Publish AFTER children are persisted. Service-layer publishing makes the
        // uSync import → save → export loop structurally impossible: the importer
        // writes through the repository directly, bypassing this method entirely.
        _eventAggregator.Publish(new SchemaMappingSavedNotification(saved.ContentTypeAlias, saved.ContentTypeKey));

        // Recompute drift AFTER publish — the export-on-save handler runs synchronously inside
        // Publish, so this reports the truth (whether the file actually landed) rather than the
        // pre-export state GetMapping saw. PersistedTo is derived from it: in-sync means the
        // mapping reached disk too.
        result.DriftStatus = _driftReporter.GetStatus(dto.ContentTypeAlias);
        result.PersistedTo = result.DriftStatus == MappingDriftStatus.InSync
            ? "database+usync"
            : "database";

        return result;
    }

    public void DeleteMapping(string contentTypeAlias)
    {
        var mapping = _repository.GetByContentTypeAlias(contentTypeAlias);
        if (mapping is null)
            return;

        _repository.Delete(mapping.Id);

        // Only publish when a mapping actually existed, so satellites don't act on
        // a no-op delete.
        _eventAggregator.Publish(new SchemaMappingDeletedNotification(mapping.ContentTypeAlias, mapping.ContentTypeKey));
    }

    public IEnumerable<PropertyMappingSuggestion> AutoMap(string contentTypeAlias, string schemaTypeName)
        => _autoMapper.SuggestMappings(contentTypeAlias, schemaTypeName);

    public Task<IEnumerable<PropertyMappingSuggestion>> AutoMapAsync(string contentTypeAlias, string schemaTypeName)
        => _autoMapper.SuggestMappingsAsync(contentTypeAlias, schemaTypeName);

    public JsonLdPreviewResponse GeneratePreview(IPublishedContent content, string? culture = null)
    {
        var response = new JsonLdPreviewResponse();

        try
        {
            // v1.4+ default: the backoffice preview matches what the Delivery
            // API and tag helper actually emit — a single Yoast-style @graph.
            // Legacy per-mapping string is only used when UseGraphModel is
            // explicitly disabled so editors don't see a different shape in
            // the preview tab than the live site renders.
            var jsonLd = _options.UseGraphModel
                ? _graphGenerator.GenerateGraphJson(content, culture)
                : _generator.GenerateJsonLdString(content, culture);
            if (jsonLd is not null)
            {
                response.JsonLd = jsonLd;
                ApplyValidation(response, jsonLd);
            }
            else
            {
                response.Errors.Add("No schema mapping found or mapping is disabled for this content type.");
            }
        }
        catch (Exception ex)
        {
            response.Errors.Add(ex.Message);
            response.Issues.Add(new ValidationIssueDto("critical", "(generation-error)", "$",
                $"JSON-LD generation failed: {ex.Message}"));
            _logger.LogError(ex, "Error generating JSON-LD preview for content {ContentId}", content.Id);
        }

        // The resolved base URL is the backoffice host in this context, so it's
        // surfaced to make the preview-vs-live @id divergence visible. Resolved
        // regardless of UseGraphModel — both paths read the same HttpContext host.
        response.ResolvedBaseUrl = _generator.GetResolvedBaseUrl();
        AppendStructuralWarnings(response, content.ContentType.Alias);

        return response;
    }

    public JsonLdPreviewResponse GenerateMockPreview(string contentTypeAlias)
    {
        // Always report the base URL the preview resolves @id against — even when no mapping
        // exists — so the preview's host context is never ambiguous (it pairs with the always-set
        // Context = "backoffice-preview").
        var response = new JsonLdPreviewResponse { ResolvedBaseUrl = _generator.GetResolvedBaseUrl() };
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
        ApplyValidation(response, response.JsonLd);
        // ResolvedBaseUrl already set at the top of the method.
        AppendStructuralWarnings(response, contentTypeAlias);
        return response;
    }

    /// <summary>
    /// Appends structural (range + reachability) warnings to a preview response
    /// as <c>warning</c> issues. These never flip <see cref="JsonLdPreviewResponse.IsValid"/>
    /// — that flag tracks Rich Results critical errors only. Safe to call when no
    /// mapping exists (no-op).
    /// </summary>
    private void AppendStructuralWarnings(JsonLdPreviewResponse response, string? contentTypeAlias)
    {
        if (string.IsNullOrWhiteSpace(contentTypeAlias))
            return;

        var dto = GetMapping(contentTypeAlias);
        if (dto is null)
            return;

        // Range warnings (GetMapping already ran the validator).
        response.Issues.AddRange(dto.Warnings);

        // Reachability: hedge that element/block types only emit when a page routes them.
        if (string.Equals(dto.Reachability, MappingReachabilityClassifier.ComposedFromBlock, StringComparison.Ordinal))
        {
            response.Issues.Add(new ValidationIssueDto(
                "warning",
                dto.SchemaTypeName,
                contentTypeAlias,
                MappingReachabilityClassifier.ComposedFromBlockWarning));
        }
    }

    /// <summary>
    /// Runs <see cref="ISchemaValidator"/> over the generated JSON-LD and
    /// fills <see cref="JsonLdPreviewResponse.Issues"/>, <see cref="JsonLdPreviewResponse.Errors"/>
    /// (legacy) and <see cref="JsonLdPreviewResponse.IsValid"/>. Safe to call
    /// on any non-null JSON-LD string — parse failures surface as a single
    /// Critical issue rather than an exception.
    /// </summary>
    private void ApplyValidation(JsonLdPreviewResponse response, string jsonLd)
    {
        var result = _validator.Validate(jsonLd);
        foreach (var issue in result.Issues)
        {
            var severity = issue.Severity switch
            {
                ValidationSeverity.Critical => "critical",
                ValidationSeverity.Warning => "warning",
                _ => "info",
            };
            response.Issues.Add(new ValidationIssueDto(severity, issue.SchemaType, issue.Path, issue.Message));

            if (issue.Severity == ValidationSeverity.Critical)
                response.Errors.Add(issue.Message);
        }

        response.IsValid = !result.HasCritical;
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

        return await BuildBlockElementTypeInfosAsync(elementTypeKeys, depth: 0, ancestorKeys: []).ConfigureAwait(false);
    }

    /// <summary>
    /// Maximum block-nesting depth walked when discovering element types, matching
    /// <see cref="Resolvers.PropertyResolverContext.MaxRecursionDepth"/>.
    /// </summary>
    private const int MaxBlockElementDiscoveryDepth = 3;

    /// <summary>
    /// Builds <see cref="BlockElementTypeInfo"/> for each element-type key, recursing into any
    /// property that is itself a Block List/Grid so nested blocks surface as
    /// <see cref="BlockElementPropertyInfo.NestedBlockElementTypes"/>. Depth-capped, and
    /// cycle-guarded by the current ancestor path so an element type that (transitively)
    /// contains itself cannot recurse forever.
    /// </summary>
    private async Task<List<BlockElementTypeInfo>> BuildBlockElementTypeInfosAsync(
        IReadOnlyCollection<Guid> elementTypeKeys, int depth, HashSet<Guid> ancestorKeys)
    {
        var result = new List<BlockElementTypeInfo>();

        foreach (var key in elementTypeKeys)
        {
            // Cycle guard: skip an element type already open on the current branch.
            if (ancestorKeys.Contains(key))
                continue;

            var elementType = _contentTypeService.Get(key);
            if (elementType is null)
                continue;

            var info = new BlockElementTypeInfo
            {
                Alias = elementType.Alias,
                Name = elementType.Name ?? elementType.Alias,
                Properties = elementType.PropertyTypes.Select(p => p.Alias).ToList(),
                PropertyInfos = []
            };

            ancestorKeys.Add(key);
            foreach (var p in elementType.PropertyTypes)
            {
                var propInfo = new BlockElementPropertyInfo
                {
                    Alias = p.Alias,
                    Name = p.Name ?? p.Alias,
                    EditorAlias = p.PropertyEditorAlias
                };

                if (depth < MaxBlockElementDiscoveryDepth
                    && SchemeWeaverConstants.PropertyEditors.BlockEditorAliases.Contains(p.PropertyEditorAlias))
                {
                    var nestedDataType = await _dataTypeService.GetAsync(p.DataTypeKey).ConfigureAwait(false);
                    if (nestedDataType is not null)
                    {
                        var nestedKeys = ExtractBlockElementTypeKeys(nestedDataType);
                        if (nestedKeys.Count > 0)
                            propInfo.NestedBlockElementTypes = await BuildBlockElementTypeInfosAsync(
                                nestedKeys, depth + 1, ancestorKeys).ConfigureAwait(false);
                    }
                }

                info.PropertyInfos.Add(propInfo);
            }
            ancestorKeys.Remove(key);

            result.Add(info);
        }

        return result;
    }

    public async Task<IEnumerable<BlockMappingSuggestion>> SuggestBlockMappingsAsync(string contentTypeAlias, string propertyAlias)
    {
        var elementTypes = await GetBlockElementTypesAsync(contentTypeAlias, propertyAlias).ConfigureAwait(false);
        return _blockSchemaSuggester.Suggest(elementTypes);
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
            IdOverride = mapping.IdOverride,
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
                DynamicRootConfig = p.DynamicRootConfig,
                TargetPieceKey = p.TargetPieceKey
            }).ToList()
        };
}
