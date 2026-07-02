using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;

namespace Umbraco.Community.SchemeWeaver.Controllers;

/// <summary>
/// Management API controller for SchemeWeaver backoffice operations.
/// Unexpected exceptions are funnelled through <see cref="HandlesServerErrorAttribute"/>
/// (500 + <c>{ error }</c> body); enumerable results are materialised inside the action so a
/// lazily-throwing sequence cannot fail during serialisation, after the filter has run.
/// </summary>
[Route("umbraco/management/api/v1/schemeweaver")]
[ApiExplorerSettings(GroupName = SchemeWeaverConstants.PackageName)]
[MapToApi("management")]
[JsonOptionsName(Constants.JsonOptionsNames.BackOffice)]
[ApiController]
// The BackOfficeAccess policy brings the OpenIddict bearer scheme (API users /
// MCP clients); the attribute adds the backoffice cookie scheme so existing
// cookie-session callers keep working. The schemes are unioned at evaluation.
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess, AuthenticationSchemes = Constants.Security.BackOfficeAuthenticationType)]
public class SchemeWeaverApiController : ControllerBase
{
    private readonly ISchemeWeaverService _service;
    private readonly IContentTypeService _contentTypeService;
    private readonly IContentService _contentService;
    private readonly IContentTypeGenerator _contentTypeGenerator;
    private readonly ISchemaAutoMapper _schemaAutoMapper;
    private readonly IMappingDriftReporter _driftReporter;
    private readonly IMappingExporter _mappingExporter;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IPropertyValueSchemaService _valueSchemaService;
    private readonly ILogger<SchemeWeaverApiController> _logger;

    public SchemeWeaverApiController(
        ISchemeWeaverService service,
        IContentTypeService contentTypeService,
        IContentService contentService,
        IContentTypeGenerator contentTypeGenerator,
        ISchemaAutoMapper schemaAutoMapper,
        IMappingDriftReporter driftReporter,
        IMappingExporter mappingExporter,
        IUmbracoContextAccessor umbracoContextAccessor,
        IPropertyValueSchemaService valueSchemaService,
        ILogger<SchemeWeaverApiController> logger)
    {
        _service = service;
        _contentTypeService = contentTypeService;
        _contentService = contentService;
        _contentTypeGenerator = contentTypeGenerator;
        _schemaAutoMapper = schemaAutoMapper;
        _driftReporter = driftReporter;
        _mappingExporter = mappingExporter;
        _umbracoContextAccessor = umbracoContextAccessor;
        _valueSchemaService = valueSchemaService;
        _logger = logger;
    }

    #region Schema Types

    [HttpGet("schema-types")]
    [ProducesResponseType(typeof(IEnumerable<SchemaTypeInfo>), StatusCodes.Status200OK)]
    [HandlesServerError("retrieving schema types")]
    public IActionResult GetSchemaTypes([FromQuery] string? search = null)
    {
        var types = string.IsNullOrWhiteSpace(search)
            ? _service.GetSchemaTypes()
            : _service.SearchSchemaTypes(search);

        return Ok(types.ToList());
    }

    [HttpGet("schema-types/{name}/properties")]
    [ProducesResponseType(typeof(IEnumerable<SchemaPropertyInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IEnumerable<RankedSchemaPropertyInfo>), StatusCodes.Status200OK)]
    [HandlesServerError("retrieving schema type properties")]
    public IActionResult GetSchemaTypeProperties(string name, [FromQuery] bool ranked = false)
    {
        if (ranked)
        {
            var rankedResults = _schemaAutoMapper.RankSchemaProperties(name);
            return Ok(rankedResults);
        }

        var properties = _service.GetSchemaProperties(name);
        return Ok(properties);
    }

    #endregion

    #region Content Types

    [HttpGet("content-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HandlesServerError("retrieving content types")]
    public IActionResult GetContentTypes()
    {
        var contentTypes = _contentTypeService.GetAll()
            .Select(ct => new
            {
                ct.Alias,
                ct.Name,
                ct.Key,
                PropertyCount = ct.CompositionPropertyTypes.Count()
            })
            .OrderBy(ct => ct.Name)
            .ToList();

        return Ok(contentTypes);
    }

    [HttpGet("content-types/{alias}/properties")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HandlesServerError("retrieving content type properties")]
    public async Task<IActionResult> GetContentTypeProperties(string alias)
    {
        var contentType = _contentTypeService.Get(alias);
        if (contentType == null) return NotFound();

        // Built-in node properties first (no real data type, so no value schema), then the
        // editor-defined properties. Uses CompositionPropertyTypes (not PropertyTypes) so
        // properties inherited from compositions — e.g. a shared "Hero" tab — are included,
        // each enriched with its Umbraco 17.4+ value JSON Schema (the actual shape its stored
        // value takes) when available, else null on older hosts.
        var properties = new List<object>();

        foreach (var bp in SchemeWeaverConstants.BuiltInProperties.All)
        {
            properties.Add(new
            {
                Alias = bp.Alias,
                Name = bp.DisplayName,
                EditorAlias = bp.EditorAlias,
                Description = (string?)null,
                ValueSchema = (string?)null,
            });
        }

        foreach (var pt in contentType.CompositionPropertyTypes)
        {
            var valueSchema = await _valueSchemaService.GetDataTypeValueSchemaAsync(pt.DataTypeKey).ConfigureAwait(false);
            properties.Add(new
            {
                Alias = pt.Alias,
                Name = pt.Name,
                EditorAlias = pt.PropertyEditorAlias,
                Description = pt.Description,
                ValueSchema = valueSchema,
            });
        }

        return Ok(properties);
    }

    [HttpGet("content-types/{contentTypeAlias}/properties/{propertyAlias}/block-types")]
    [ProducesResponseType(typeof(IEnumerable<BlockElementTypeInfo>), StatusCodes.Status200OK)]
    [HandlesServerError("retrieving block element types")]
    public async Task<IActionResult> GetBlockElementTypes(string contentTypeAlias, string propertyAlias)
    {
        var blockTypes = await _service.GetBlockElementTypesAsync(contentTypeAlias, propertyAlias).ConfigureAwait(false);
        return Ok(blockTypes);
    }

    [HttpPost("content-types/{contentTypeAlias}/properties/{propertyAlias}/block-suggest")]
    [ProducesResponseType(typeof(IEnumerable<BlockMappingSuggestion>), StatusCodes.Status200OK)]
    [HandlesServerError("suggesting block mappings")]
    public async Task<IActionResult> SuggestBlockMappings(string contentTypeAlias, string propertyAlias)
    {
        var suggestions = await _service.SuggestBlockMappingsAsync(contentTypeAlias, propertyAlias).ConfigureAwait(false);
        return Ok(suggestions);
    }

    #endregion

    #region Mappings

    [HttpGet("mappings")]
    [ProducesResponseType(typeof(IEnumerable<SchemaMappingDto>), StatusCodes.Status200OK)]
    [HandlesServerError("retrieving schema mappings")]
    public IActionResult GetMappings()
    {
        // GetAllMappings is lazy (per-item drift/reachability probes) — materialise so a
        // throwing probe surfaces here, inside the exception filter's reach.
        return Ok(_service.GetAllMappings().ToList());
    }

    [HttpGet("mappings/{contentTypeAlias}")]
    [ProducesResponseType(typeof(SchemaMappingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HandlesServerError("retrieving the schema mapping")]
    public IActionResult GetMapping(string contentTypeAlias)
    {
        var mapping = _service.GetMapping(contentTypeAlias);
        if (mapping == null) return NotFound();
        return Ok(mapping);
    }

    [HttpPost("mappings")]
    [ProducesResponseType(typeof(SchemaMappingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HandlesServerError("saving the schema mapping")]
    public IActionResult SaveMapping([FromBody] SchemaMappingDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ContentTypeAlias))
            return BadRequest("ContentTypeAlias is required.");

        if (string.IsNullOrWhiteSpace(dto.SchemaTypeName))
            return BadRequest("SchemaTypeName is required.");

        var saved = _service.SaveMapping(dto);
        return Ok(saved);
    }

    [HttpDelete("mappings/{contentTypeAlias}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [HandlesServerError("deleting the schema mapping")]
    public IActionResult DeleteMapping(string contentTypeAlias)
    {
        _service.DeleteMapping(contentTypeAlias);
        return NoContent();
    }

    [HttpGet("mappings/drift")]
    [ProducesResponseType(typeof(MappingDriftReportDto), StatusCodes.Status200OK)]
    [HandlesServerError("computing mapping drift")]
    public IActionResult GetMappingDrift()
    {
        return Ok(_driftReporter.GetReport());
    }

    [HttpPost("mappings/export")]
    [ProducesResponseType(typeof(MappingExportResultDto), StatusCodes.Status200OK)]
    [HandlesServerError("exporting mappings to uSync")]
    public IActionResult ExportMappings([FromBody] MappingExportRequest? request = null)
    {
        return Ok(_mappingExporter.Export(request?.ContentTypeAlias));
    }

    [HttpPost("mappings/{contentTypeAlias}/auto-map")]
    [ProducesResponseType(typeof(IEnumerable<PropertyMappingSuggestion>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HandlesServerError("generating auto-map suggestions")]
    public async Task<IActionResult> AutoMap(string contentTypeAlias, [FromQuery] string schemaTypeName)
    {
        if (string.IsNullOrWhiteSpace(schemaTypeName))
            return BadRequest("schemaTypeName query parameter is required.");

        // Awaits the seam: heuristic by default, AI when the SchemeWeaver.AI satellite overrides it.
        var suggestions = await _service.AutoMapAsync(contentTypeAlias, schemaTypeName).ConfigureAwait(false);
        return Ok(suggestions);
    }

    [HttpPost("mappings/{contentTypeAlias}/preview")]
    [ProducesResponseType(typeof(JsonLdPreviewResponse), StatusCodes.Status200OK)]
    [HandlesServerError("generating the JSON-LD preview")]
    public IActionResult Preview(string contentTypeAlias, [FromQuery] Guid? contentKey = null, [FromQuery] Guid? blockInstanceKey = null, [FromQuery] string? culture = null)
    {
        // When a content key is provided, generate real JSON-LD from published content
        if (contentKey.HasValue && contentKey.Value != Guid.Empty)
        {
            if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Unable to access Umbraco context.");
            }

            var content = umbracoContext.Content?.GetById(contentKey.Value);
            if (content == null) return NotFound("Content not found.");

            // Block-instance preview: render the real JSON-LD a single nested block contributes
            // to its page (via the page mapping's route for that block type).
            if (blockInstanceKey is { } bik && bik != Guid.Empty)
            {
                return Ok(_service.GenerateBlockInstancePreview(content, bik, culture));
            }

            var preview = _service.GeneratePreview(content, culture);
            return Ok(preview);
        }

        // No content key — return mock preview based on mapping configuration
        var mockPreview = _service.GenerateMockPreview(contentTypeAlias);
        return Ok(mockPreview);
    }

    #endregion

    #region Server Context

    [HttpGet("server-context")]
    [ProducesResponseType(typeof(ServerContextDto), StatusCodes.Status200OK)]
    public IActionResult GetServerContext()
    {
        // Lets callers (e.g. the MCP) tell a populated site from an empty sandbox/TestHost
        // before trusting a render — the root cause of "the tool said fine but it was empty".
        var dto = new ServerContextDto();

        try
        {
            // A published root implies a routable tree (descendants can't publish without
            // published ancestors), so this is a reliable "populated vs empty sandbox" signal.
            dto.HasPublishedContent = _contentService.GetRootContent().Any(c => c.Published);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not determine published content state for server context");
        }

        var entryName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        dto.IsTestHost = entryName?.Contains("TestHost", StringComparison.OrdinalIgnoreCase) == true;

        return Ok(dto);
    }

    #endregion

    #region Content Type Generation (Phase 2)

    [HttpPost("generate-content-type")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HandlesServerError("generating the content type")]
    public async Task<IActionResult> GenerateContentType([FromBody] ContentTypeGenerationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SchemaTypeName))
            return BadRequest("SchemaTypeName is required.");

        if (string.IsNullOrWhiteSpace(request.DocumentTypeName))
            return BadRequest("DocumentTypeName is required.");

        var key = await _contentTypeGenerator.GenerateContentTypeAsync(request, cancellationToken).ConfigureAwait(false);
        return Ok(new { Key = key });
    }

    #endregion
}
