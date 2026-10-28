using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Seo.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Templates;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Seo.Services;

public sealed class SeoHealthCheckService : ISeoHealthCheckService
{
    private const string CacheKey = "schemeweaver:seo:health-report";

    private readonly ISchemaMappingRepository _repository;
    private readonly IContentTypeService _contentTypeService;
    private readonly ISeoTemplateCatalogue _catalogue;
    private readonly IPublishedContentOfTypeQuery _published;
    private readonly ISchemeWeaverService _service;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<SchemeWeaverSeoOptions> _options;
    private readonly ILogger<SeoHealthCheckService> _logger;

    public SeoHealthCheckService(
        ISchemaMappingRepository repository,
        IContentTypeService contentTypeService,
        ISeoTemplateCatalogue catalogue,
        IPublishedContentOfTypeQuery published,
        ISchemeWeaverService service,
        IMemoryCache cache,
        IOptionsMonitor<SchemeWeaverSeoOptions> options,
        ILogger<SeoHealthCheckService> logger)
    {
        _repository = repository;
        _contentTypeService = contentTypeService;
        _catalogue = catalogue;
        _published = published;
        _service = service;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public SeoHealthReportDto Run(bool force)
    {
        if (!force && _cache.TryGetValue(CacheKey, out SeoHealthReportDto? cached) && cached is not null)
        {
            return new SeoHealthReportDto
            {
                GeneratedAt = cached.GeneratedAt,
                Cached = true,
                ContentTypesChecked = cached.ContentTypesChecked,
                Critical = cached.Critical,
                Warning = cached.Warning,
                Items = cached.Items,
            };
        }

        var report = Build();
        var duration = _options.CurrentValue.HealthCheckCacheDuration;
        if (duration > TimeSpan.Zero)
            _cache.Set(CacheKey, report, duration);
        return report;
    }

    public void Evict() => _cache.Remove(CacheKey);

    private SeoHealthReportDto Build()
    {
        var items = new List<SeoHealthItemDto>();
        var contentTypes = _contentTypeService.GetAll().ToDictionary(ct => ct.Alias, StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in _repository.GetAll().Where(m => m.IsEnabled).OrderBy(m => m.ContentTypeAlias))
        {
            if (!contentTypes.TryGetValue(mapping.ContentTypeAlias, out var contentType) || contentType.IsElement)
                continue;

            var template = _catalogue.FindForSchemaType(mapping.SchemaTypeName);
            var sample = _published.MostRecentlyPublished(mapping.ContentTypeAlias);
            if (sample is null)
            {
                items.Add(new SeoHealthItemDto
                {
                    Alias = mapping.ContentTypeAlias,
                    Name = contentType.Name ?? contentType.Alias,
                    SchemaTypeName = mapping.SchemaTypeName,
                    TemplateId = template?.Id,
                    Status = "no-published-page",
                });
                continue;
            }

            List<ValidationIssueDto> issues;
            try
            {
                issues = _service.GeneratePreview(sample).Issues;
            }
            catch (Exception ex)
            {
                // A broken preview is itself a finding, not a reason to lose the whole report.
                _logger.LogWarning(ex, "SchemeWeaver SEO health check could not preview {Alias}", mapping.ContentTypeAlias);
                issues = [new ValidationIssueDto("critical", mapping.SchemaTypeName, "$", "The preview could not be generated: " + ex.Message)];
            }

            var critical = issues.Count(i => i.Severity == "critical");
            var warning = issues.Count(i => i.Severity == "warning");
            items.Add(new SeoHealthItemDto
            {
                Alias = mapping.ContentTypeAlias,
                Name = contentType.Name ?? contentType.Alias,
                SchemaTypeName = mapping.SchemaTypeName,
                TemplateId = template?.Id,
                SamplePageKey = sample.Key,
                SamplePageName = sample.Name,
                Critical = critical,
                Warning = warning,
                Issues = issues.Where(i => i.Severity is "critical" or "warning").ToList(),
                Status = critical > 0 ? "needs-attention" : "ok",
            });
        }

        return new SeoHealthReportDto
        {
            GeneratedAt = DateTime.UtcNow,
            Cached = false,
            ContentTypesChecked = items.Count,
            Critical = items.Sum(i => i.Critical),
            Warning = items.Sum(i => i.Warning),
            Items = items,
        };
    }
}
