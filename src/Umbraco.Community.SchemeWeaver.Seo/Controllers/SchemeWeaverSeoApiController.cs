using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.SchemeWeaver.Controllers;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Authorization;
using Umbraco.Community.SchemeWeaver.Seo.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Overrides;
using Umbraco.Community.SchemeWeaver.Seo.Services;
using Umbraco.Community.SchemeWeaver.Seo.SiteIdentity;
using Umbraco.Community.SchemeWeaver.Seo.Templates;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Validation;

namespace Umbraco.Community.SchemeWeaver.Seo.Controllers;

/// <summary>
/// Management API for the SEO Schema section. Every write ends up in the ordinary SchemeWeaver
/// tables (mappings) or this package's own identity / override storage; the core's management
/// API stays the one source of truth for mappings. Errors use the core's frozen <c>{ error }</c>
/// body via <see cref="HandlesServerErrorAttribute"/>.
/// </summary>
[Route("umbraco/management/api/v1/schemeweaver-seo")]
[ApiExplorerSettings(GroupName = "SchemeWeaverSeo")]
[MapToApi("management")]
[JsonOptionsName(Constants.JsonOptionsNames.BackOffice)]
[ApiController]
[Authorize(Policy = SeoAuthorizationPolicies.SectionAccess, AuthenticationSchemes = Constants.Security.BackOfficeAuthenticationType)]
public class SchemeWeaverSeoApiController : ControllerBase
{
    private readonly ISeoTemplateCatalogue _catalogue;
    private readonly ISiteIdentityStore _identityStore;
    private readonly ISeoPageOverrideRepository _overrides;
    private readonly ISeoContentTypeStatusService _status;
    private readonly ISeoSlotProjector _projector;
    private readonly ISeoHealthCheckService _health;
    private readonly IPublishedContentOfTypeQuery _published;
    private readonly ISchemeWeaverService _service;
    private readonly IContentTypeService _contentTypeService;
    private readonly IContentService _contentService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly ISiteOriginResolver _originResolver;
    private readonly IMappingExporter _mappingExporter;
    private readonly IJsonLdBlocksProvider _blocksProvider;
    private readonly ISchemaRangeChecker _rangeChecker;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;
    private readonly IOptions<SchemeWeaverOptions> _coreOptions;
    private readonly IOptions<SchemeWeaverSeoOptions> _seoOptions;
    private readonly Umbraco.Cms.Core.Services.Navigation.IDocumentNavigationQueryService _navigation;

    public SchemeWeaverSeoApiController(
        ISeoTemplateCatalogue catalogue,
        ISiteIdentityStore identityStore,
        ISeoPageOverrideRepository overrides,
        ISeoContentTypeStatusService status,
        ISeoSlotProjector projector,
        ISeoHealthCheckService health,
        IPublishedContentOfTypeQuery published,
        ISchemeWeaverService service,
        IContentTypeService contentTypeService,
        IContentService contentService,
        IUmbracoContextAccessor umbracoContextAccessor,
        IPublishedUrlProvider urlProvider,
        ISiteOriginResolver originResolver,
        IMappingExporter mappingExporter,
        IJsonLdBlocksProvider blocksProvider,
        ISchemaRangeChecker rangeChecker,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
        IOptions<SchemeWeaverOptions> coreOptions,
        IOptions<SchemeWeaverSeoOptions> seoOptions,
        Umbraco.Cms.Core.Services.Navigation.IDocumentNavigationQueryService navigation)
    {
        _navigation = navigation;
        _catalogue = catalogue;
        _identityStore = identityStore;
        _overrides = overrides;
        _status = status;
        _projector = projector;
        _health = health;
        _published = published;
        _service = service;
        _contentTypeService = contentTypeService;
        _contentService = contentService;
        _umbracoContextAccessor = umbracoContextAccessor;
        _urlProvider = urlProvider;
        _originResolver = originResolver;
        _mappingExporter = mappingExporter;
        _blocksProvider = blocksProvider;
        _rangeChecker = rangeChecker;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
        _coreOptions = coreOptions;
        _seoOptions = seoOptions;
    }

    // ------------------------------------------------------------------ catalogue & context

    [HttpGet("templates")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HandlesServerError("retrieving the content-kind catalogue")]
    public IActionResult GetTemplates() => Ok(new SeoTemplateCatalogueDto
    {
        Templates = _catalogue.All,
        Retired = _catalogue.Retired,
        BusinessTypes = _catalogue.BusinessTypes,
    });

    [HttpGet("server-context")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HandlesServerError("retrieving the SEO server context")]
    public IActionResult GetServerContext()
    {
        var core = _coreOptions.Value;
        var hasPublished = _umbracoContextAccessor.TryGetUmbracoContext(out var ctx)
                           && ctx.Content is not null
                           && _navigation.TryGetRootKeys(out var rootKeys)
                           && rootKeys.Any(k => ctx.Content.GetById(k) is not null);
        return Ok(new SeoServerContextDto
        {
            SiteUrl = SafeOrigin(),
            SiteIdentityConfigured = _identityStore.Get()?.IsConfigured == true,
            HideDeveloperJsonLdTab = _seoOptions.Value.HideDeveloperJsonLdTab,
            MappingsManagedAsCode = _mappingExporter.IsAvailable,
            USyncBootImport = core.USyncBootImport.ToString(),
            ExportMappingsToUSyncOnSave = core.ExportMappingsToUSyncOnSave,
            UseGraphModel = core.UseGraphModel,
            EmitBreadcrumbsInDeliveryApi = core.EmitBreadcrumbsInDeliveryApi,
            PublicSiteUrl = core.PublicSiteUrl,
            HasPublishedContent = hasPublished,
        });
    }

    // ------------------------------------------------------------------ site identity

    [HttpGet("site-identity")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HandlesServerError("retrieving the site identity")]
    public IActionResult GetSiteIdentity() => Ok(_identityStore.Get() ?? new SiteIdentityDto { IsConfigured = false });

    [HttpPut("site-identity")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HandlesServerError("saving the site identity")]
    public IActionResult SaveSiteIdentity([FromBody] SiteIdentityDto identity)
    {
        var errors = _identityStore.Save(identity, CurrentUserKey());
        if (errors.Count > 0)
            return BadRequest(new { error = string.Join(" ", errors), errors });

        _health.Evict();
        return Ok(_identityStore.Get());
    }

    [HttpDelete("site-identity")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [HandlesServerError("clearing the site identity")]
    public IActionResult DeleteSiteIdentity()
    {
        _identityStore.Delete();
        _health.Evict();
        return NoContent();
    }

    // ------------------------------------------------------------------ content types

    [HttpGet("content-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HandlesServerError("retrieving content type statuses")]
    public IActionResult GetContentTypes() => Ok(_status.GetAll());

    [HttpGet("content-types/{alias}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HandlesServerError("retrieving a content type status")]
    public IActionResult GetContentType(string alias)
    {
        var status = _status.Get(alias);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>
    /// Starts (or revisits) the set-up of a content type: runs the core auto-mapper for the
    /// chosen kind and returns the slots, pre-filled from the existing mapping, then the
    /// suggestions, then the slot defaults.
    /// </summary>
    [HttpPost("content-types/{alias}/setup")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HandlesServerError("preparing the content type set-up")]
    public async Task<IActionResult> Setup(string alias, [FromBody] SeoSetupRequest request)
    {
        var contentType = _contentTypeService.Get(alias);
        if (contentType is null)
            return NotFound(new { error = $"Content type '{alias}' was not found." });

        var template = _catalogue.Get(request.TemplateId);
        if (template is null)
            return NotFound(new { error = $"Content kind '{request.TemplateId}' was not found." });

        if (!TryResolveSchemaType(template, request.SchemaTypeName, out var schemaType, out var problem))
            return BadRequest(new { error = problem });

        var existing = _service.GetMapping(alias);
        var suggestions = (await _service.AutoMapAsync(alias, schemaType).ConfigureAwait(false)).ToList();
        var editors = EditorAliases(contentType);
        var projection = _projector.Project(template, schemaType, existing, suggestions, editors, _identityStore.Get()?.IsConfigured == true);
        return Ok(projection);
    }

    /// <summary>Saves the slots as an ordinary SchemeWeaver mapping (rows the kind does not own are preserved).</summary>
    [HttpPut("content-types/{alias}/mapping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HandlesServerError("saving the content type mapping")]
    public IActionResult SaveMapping(string alias, [FromBody] SeoMappingSaveRequest request)
    {
        var contentType = _contentTypeService.Get(alias);
        if (contentType is null)
            return NotFound(new { error = $"Content type '{alias}' was not found." });

        var template = _catalogue.Get(request.TemplateId);
        if (template is null)
            return NotFound(new { error = $"Content kind '{request.TemplateId}' was not found." });

        if (!TryResolveSchemaType(template, request.SchemaTypeName, out var schemaType, out var problem))
            return BadRequest(new { error = problem });

        var existing = _service.GetMapping(alias);
        var dto = _projector.ToMappingDto(template, alias, contentType.Key, schemaType, request, existing, EditorAliases(contentType));
        _service.SaveMapping(dto);
        _health.Evict();
        return Ok(_status.Get(alias));
    }

    [HttpDelete("content-types/{alias}/mapping")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [HandlesServerError("clearing the content type mapping")]
    public IActionResult DeleteMapping(string alias)
    {
        _service.DeleteMapping(alias);
        _health.Evict();
        return NoContent();
    }

    [HttpGet("content-types/{alias}/sample-pages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HandlesServerError("finding sample pages")]
    public IActionResult GetSamplePages(string alias, [FromQuery] int take = 5) =>
        Ok(_published.RecentlyPublished(alias, Math.Clamp(take, 1, 25)).Select(ToSample).ToList());

    // ------------------------------------------------------------------ pages

    [HttpGet("pages/{contentKey:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HandlesServerError("retrieving a page override")]
    public IActionResult GetPageOverride(Guid contentKey)
    {
        var row = _overrides.Get(contentKey);
        return row is null ? NotFound() : Ok(ToDto(row));
    }

    [HttpPut("pages/{contentKey:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HandlesServerError("saving a page override")]
    public IActionResult SavePageOverride(Guid contentKey, [FromBody] SeoPageOverrideRequest request)
    {
        var content = _contentService.GetById(contentKey);
        if (content is null)
            return NotFound(new { error = "The page was not found." });

        var mapping = _service.GetMapping(content.ContentType.Alias);
        if (mapping is null)
            return NotFound(new { error = $"The page type '{content.ContentType.Alias}' has not been set up for structured data." });

        var type = string.IsNullOrWhiteSpace(request.SchemaTypeName) ? null : request.SchemaTypeName.Trim();
        if (type is not null && !string.Equals(type, mapping.SchemaTypeName, StringComparison.OrdinalIgnoreCase))
        {
            var template = _catalogue.FindForSchemaType(mapping.SchemaTypeName);
            var allowed = template is null
                ? _rangeChecker.IsInRange(type, [mapping.SchemaTypeName])
                : _catalogue.AllowedPageTypes(template).Any(v => string.Equals(v.SchemaType, type, StringComparison.OrdinalIgnoreCase))
                  && _rangeChecker.IsInRange(type, [mapping.SchemaTypeName]);
            if (!allowed)
                return BadRequest(new { error = $"'{type}' is not an allowed page type for '{mapping.SchemaTypeName}'." });
        }

        if (type is null && !request.IsExcluded)
        {
            // Nothing to override: clear any stored row rather than storing a no-op.
            _overrides.Delete(contentKey);
            InvalidateFor(contentKey, mapping);
            return Ok(new SeoPageOverrideDto { ContentKey = contentKey });
        }

        var saved = _overrides.Upsert(new SeoPageOverride
        {
            ContentKey = contentKey,
            SchemaTypeName = type,
            IsExcluded = request.IsExcluded,
            UpdatedByUserKey = CurrentUserKey(),
        });
        InvalidateFor(contentKey, mapping);
        return Ok(ToDto(saved));
    }

    [HttpDelete("pages/{contentKey:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [HandlesServerError("clearing a page override")]
    public IActionResult DeletePageOverride(Guid contentKey)
    {
        _overrides.Delete(contentKey);
        var content = _contentService.GetById(contentKey);
        InvalidateFor(contentKey, content is null ? null : _service.GetMapping(content.ContentType.Alias));
        return NoContent();
    }

    /// <summary>Everything the content item's Schema tab shows, preview included.</summary>
    [HttpGet("pages/{contentKey:guid}/summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HandlesServerError("summarising a page")]
    public IActionResult GetPageSummary(Guid contentKey, [FromQuery] string? culture = null)
    {
        var content = _contentService.GetById(contentKey);
        if (content is null)
            return NotFound(new { error = "The page was not found." });

        var alias = content.ContentType.Alias;
        var mapping = _service.GetMapping(alias);
        var template = mapping is null ? null : _catalogue.FindForSchemaType(mapping.SchemaTypeName);
        var row = _overrides.Get(contentKey);
        var excluded = row?.IsExcluded == true;
        // Same rule as SeoContentMappingOverrideProvider: an override counts when it is still a
        // subtype of what the mapping emits.
        var effective = row?.SchemaTypeName is { Length: > 0 } t && mapping is not null
                        && _rangeChecker.IsInRange(t, [mapping.SchemaTypeName])
            ? t
            : mapping?.SchemaTypeName;

        IPublishedContent? published = null;
        if (_umbracoContextAccessor.TryGetUmbracoContext(out var ctx))
            published = ctx.Content?.GetById(contentKey);

        JsonLdPreviewResponse? preview = null;
        if (published is not null && mapping is not null && !excluded)
            preview = _service.GeneratePreview(published, culture);

        return Ok(new SeoPageSummaryDto
        {
            ContentKey = contentKey,
            ContentTypeAlias = alias,
            ContentTypeName = content.ContentType.Name ?? alias,
            HasMapping = mapping is not null,
            TemplateId = template?.Id,
            TemplateLabel = template?.Label,
            DefaultSchemaTypeName = mapping?.SchemaTypeName,
            EffectiveSchemaTypeName = excluded ? null : effective,
            AllowedPageTypes = template is null ? [] : _catalogue.AllowedPageTypes(template),
            Override = row is null ? null : ToDto(row),
            IsExcluded = excluded,
            IsPublished = published is not null,
            PublicUrl = published is null ? null : SafeUrl(published, culture),
            Preview = preview,
        });
    }

    // ------------------------------------------------------------------ health

    [HttpPost("health-check")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HandlesServerError("running the site health check")]
    public IActionResult RunHealthCheck([FromQuery] bool force = false) => Ok(_health.Run(force));

    // ------------------------------------------------------------------ helpers

    private bool TryResolveSchemaType(SeoTemplate template, string? requested, out string schemaType, out string problem)
    {
        problem = string.Empty;
        if (string.IsNullOrWhiteSpace(requested))
        {
            schemaType = template.SchemaType;
            return true;
        }

        var match = _catalogue.AllowedPageTypes(template)
            .FirstOrDefault(v => string.Equals(v.SchemaType, requested.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null && !string.Equals(requested.Trim(), template.SchemaType, StringComparison.OrdinalIgnoreCase))
        {
            schemaType = template.SchemaType;
            problem = $"'{requested}' is not a variant of the '{template.Label}' kind.";
            return false;
        }

        schemaType = match?.SchemaType ?? template.SchemaType;
        return true;
    }

    private static IReadOnlyDictionary<string, string> EditorAliases(Umbraco.Cms.Core.Models.IContentType contentType)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in contentType.CompositionPropertyTypes)
            map[property.Alias] = property.PropertyEditorAlias;
        foreach (var builtIn in SchemeWeaverConstants.BuiltInProperties.All)
            map[builtIn.Alias] = builtIn.EditorAlias;
        return map;
    }

    private void InvalidateFor(Guid contentKey, SchemaMappingDto? mapping)
    {
        if (mapping?.IsInherited == true)
            _blocksProvider.InvalidateAll();
        else
            _blocksProvider.Invalidate(contentKey);
        _health.Evict();
    }

    private Guid? CurrentUserKey() => _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Key;

    private string? SafeOrigin()
    {
        try
        {
            return _originResolver.ResolveOrigin()?.ToString().TrimEnd('/');
        }
        catch
        {
            return null;
        }
    }

    private string? SafeUrl(IPublishedContent content, string? culture)
    {
        try
        {
            var url = _urlProvider.GetUrl(content, UrlMode.Absolute, culture);
            return string.IsNullOrWhiteSpace(url) || url == "#" ? null : url;
        }
        catch
        {
            return null;
        }
    }

    private SeoSamplePageDto ToSample(IPublishedContent content) => new()
    {
        Key = content.Key,
        Name = content.Name,
        Url = SafeUrl(content, null),
        UpdateDate = content.UpdateDate,
    };

    private static SeoPageOverrideDto ToDto(SeoPageOverride row) => new()
    {
        ContentKey = row.ContentKey,
        SchemaTypeName = row.SchemaTypeName,
        IsExcluded = row.IsExcluded,
        UpdatedDate = row.UpdatedDate,
    };
}
