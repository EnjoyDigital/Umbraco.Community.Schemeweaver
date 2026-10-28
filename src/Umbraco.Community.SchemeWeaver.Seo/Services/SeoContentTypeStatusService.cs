using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Seo.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Templates;
using static Umbraco.Community.SchemeWeaver.Seo.SchemeWeaverSeoConstants;

namespace Umbraco.Community.SchemeWeaver.Seo.Services;

/// <summary>
/// One row per document type for the Content types view. Reads the repository directly (not the
/// core service) so a page of 50 types does not run 50 drift/reachability probes.
/// </summary>
public sealed class SeoContentTypeStatusService : ISeoContentTypeStatusService
{
    private readonly IContentTypeService _contentTypeService;
    private readonly ISchemaMappingRepository _repository;
    private readonly ISeoTemplateCatalogue _catalogue;
    private readonly ISeoSlotProjector _projector;
    private readonly IPublishedContentOfTypeQuery _published;

    public SeoContentTypeStatusService(
        IContentTypeService contentTypeService,
        ISchemaMappingRepository repository,
        ISeoTemplateCatalogue catalogue,
        ISeoSlotProjector projector,
        IPublishedContentOfTypeQuery published)
    {
        _contentTypeService = contentTypeService;
        _repository = repository;
        _catalogue = catalogue;
        _projector = projector;
        _published = published;
    }

    public IReadOnlyList<SeoContentTypeStatusDto> GetAll()
    {
        var mappings = _repository.GetAll().ToDictionary(m => m.ContentTypeAlias, StringComparer.OrdinalIgnoreCase);
        var propertyRows = _repository.GetAllPropertyMappingsByMappingId();

        return _contentTypeService.GetAll()
            .OrderBy(ct => ct.Name)
            .Select(ct => Build(ct, mappings.GetValueOrDefault(ct.Alias), propertyRows))
            .ToList();
    }

    public SeoContentTypeStatusDto? Get(string contentTypeAlias)
    {
        var contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
            return null;

        return Build(contentType, _repository.GetByContentTypeAlias(contentTypeAlias), _repository.GetAllPropertyMappingsByMappingId());
    }

    private SeoContentTypeStatusDto Build(IContentType ct, SchemaMapping? mapping, IReadOnlyDictionary<int, List<PropertyMapping>> propertyRows)
    {
        SeoMappingSummaryDto? summary = null;
        var status = ContentTypeStatus.NotSetUp;

        if (mapping is not null)
        {
            var rows = propertyRows.TryGetValue(mapping.Id, out var list) ? list : [];
            var dto = ToDto(mapping, rows);
            var template = _catalogue.FindForSchemaType(mapping.SchemaTypeName);
            if (template is null)
            {
                summary = new SeoMappingSummaryDto { SchemaTypeName = mapping.SchemaTypeName, IsEnabled = mapping.IsEnabled, AdvancedRowCount = rows.Count };
                status = ContentTypeStatus.DeveloperManaged;
            }
            else
            {
                summary = _projector.Summarise(template, dto);
                status = !mapping.IsEnabled || summary.RequiredFilled < summary.RequiredTotal || template.IsRetired
                    ? ContentTypeStatus.NeedsAttention
                    : ContentTypeStatus.SetUp;
            }
        }

        return new SeoContentTypeStatusDto
        {
            Alias = ct.Alias,
            Name = ct.Name ?? ct.Alias,
            Key = ct.Key,
            Icon = ct.Icon,
            IsElement = ct.IsElement,
            HasTemplate = ct.DefaultTemplate is not null || ct.AllowedTemplates.Any(),
            AllowedAsRoot = ct.AllowedAsRoot,
            PublishedCount = ct.IsElement ? 0 : _published.PublishedCount(ct.Alias),
            Status = status,
            Mapping = summary,
        };
    }

    internal static SchemaMappingDto ToDto(SchemaMapping mapping, IEnumerable<PropertyMapping> rows) => new()
    {
        ContentTypeAlias = mapping.ContentTypeAlias,
        ContentTypeKey = mapping.ContentTypeKey,
        SchemaTypeName = mapping.SchemaTypeName,
        IsEnabled = mapping.IsEnabled,
        IsInherited = mapping.IsInherited,
        IdOverride = mapping.IdOverride,
        PropertyMappings = rows.Select(r => new PropertyMappingDto
        {
            SchemaPropertyName = r.SchemaPropertyName,
            SourceType = r.SourceType,
            ContentTypePropertyAlias = r.ContentTypePropertyAlias,
            SourceContentTypeAlias = r.SourceContentTypeAlias,
            TransformType = r.TransformType,
            IsAutoMapped = r.IsAutoMapped,
            StaticValue = r.StaticValue,
            NestedSchemaTypeName = r.NestedSchemaTypeName,
            ResolverConfig = r.ResolverConfig,
            DynamicRootConfig = r.DynamicRootConfig,
            TargetPieceKey = r.TargetPieceKey,
        }).ToList(),
    };
}
