using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Controllers;

/// <summary>
/// Management API controller for SchemeWeaver backoffice operations.
/// </summary>
[Route("umbraco/management/api/v1/schemeweaver")]
[ApiExplorerSettings(GroupName = SchemeWeaverConstants.PackageName)]
[MapToApi("management")]
[ApiController]
[Authorize(AuthenticationSchemes = Constants.Security.BackOfficeAuthenticationType)]
public class SchemeWeaverApiController : ControllerBase
{
    private readonly ISchemeWeaverService _service;
    private readonly IContentTypeService _contentTypeService;
    private readonly IContentTypeGenerator _contentTypeGenerator;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly ILogger<SchemeWeaverApiController> _logger;

    public SchemeWeaverApiController(
        ISchemeWeaverService service,
        IContentTypeService contentTypeService,
        IContentTypeGenerator contentTypeGenerator,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILogger<SchemeWeaverApiController> logger)
    {
        _service = service;
        _contentTypeService = contentTypeService;
        _contentTypeGenerator = contentTypeGenerator;
        _umbracoContextAccessor = umbracoContextAccessor;
        _logger = logger;
    }

    #region Schema Types

    [HttpGet("schema-types")]
    [ProducesResponseType(typeof(IEnumerable<SchemaTypeInfo>), StatusCodes.Status200OK)]
    public IActionResult GetSchemaTypes([FromQuery] string? search = null)
    {
        var types = string.IsNullOrWhiteSpace(search)
            ? _service.GetSchemaTypes()
            : _service.SearchSchemaTypes(search);

        return Ok(types);
    }

    [HttpGet("schema-types/{name}/properties")]
    [ProducesResponseType(typeof(IEnumerable<SchemaPropertyInfo>), StatusCodes.Status200OK)]
    public IActionResult GetSchemaTypeProperties(string name)
    {
        var properties = _service.GetSchemaProperties(name);
        return Ok(properties);
    }

    #endregion

    #region Content Types

    [HttpGet("content-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetContentTypes()
    {
        var contentTypes = _contentTypeService.GetAll()
            .Select(ct => new
            {
                ct.Alias,
                ct.Name,
                ct.Key,
                PropertyCount = ct.PropertyTypes.Count()
            })
            .OrderBy(ct => ct.Name);

        return Ok(contentTypes);
    }

    [HttpGet("content-types/{alias}/properties")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetContentTypeProperties(string alias)
    {
        var contentType = _contentTypeService.Get(alias);
        if (contentType == null) return NotFound();

        var properties = contentType.PropertyTypes.Select(pt => new
        {
            pt.Alias,
            pt.Name,
            EditorAlias = pt.PropertyEditorAlias,
            pt.Description
        });

        return Ok(properties);
    }

    [HttpGet("content-types/{contentTypeAlias}/properties/{propertyAlias}/block-types")]
    [ProducesResponseType(typeof(IEnumerable<BlockElementTypeInfo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlockElementTypes(string contentTypeAlias, string propertyAlias)
    {
        var blockTypes = await _service.GetBlockElementTypesAsync(contentTypeAlias, propertyAlias).ConfigureAwait(false);
        return Ok(blockTypes);
    }

    #endregion

    #region Mappings

    [HttpGet("mappings")]
    [ProducesResponseType(typeof(IEnumerable<SchemaMappingDto>), StatusCodes.Status200OK)]
    public IActionResult GetMappings()
    {
        return Ok(_service.GetAllMappings());
    }

    [HttpGet("mappings/{contentTypeAlias}")]
    [ProducesResponseType(typeof(SchemaMappingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetMapping(string contentTypeAlias)
    {
        var mapping = _service.GetMapping(contentTypeAlias);
        if (mapping == null) return NotFound();
        return Ok(mapping);
    }

    [HttpPost("mappings")]
    [ProducesResponseType(typeof(SchemaMappingDto), StatusCodes.Status200OK)]
    public IActionResult SaveMapping([FromBody] SchemaMappingDto dto)
    {
        var saved = _service.SaveMapping(dto);
        return Ok(saved);
    }

    [HttpDelete("mappings/{contentTypeAlias}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult DeleteMapping(string contentTypeAlias)
    {
        _service.DeleteMapping(contentTypeAlias);
        return NoContent();
    }

    [HttpPost("mappings/{contentTypeAlias}/auto-map")]
    [ProducesResponseType(typeof(IEnumerable<PropertyMappingSuggestion>), StatusCodes.Status200OK)]
    public IActionResult AutoMap(string contentTypeAlias, [FromQuery] string schemaTypeName)
    {
        var suggestions = _service.AutoMap(contentTypeAlias, schemaTypeName);
        return Ok(suggestions);
    }

    [HttpPost("mappings/{contentTypeAlias}/preview")]
    [ProducesResponseType(typeof(JsonLdPreviewResponse), StatusCodes.Status200OK)]
    public IActionResult Preview(string contentTypeAlias, [FromQuery] Guid? contentKey = null)
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

            var preview = _service.GeneratePreview(content);
            return Ok(preview);
        }

        // No content key — return mock preview based on mapping configuration
        var mockPreview = _service.GenerateMockPreview(contentTypeAlias);
        return Ok(mockPreview);
    }

    #endregion

    #region Content Type Generation (Phase 2)

    [HttpPost("generate-content-type")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateContentType([FromBody] ContentTypeGenerationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var key = await _contentTypeGenerator.GenerateContentTypeAsync(request, cancellationToken).ConfigureAwait(false);
            return Ok(new { Key = key });
        }
        catch (NotImplementedException)
        {
            return StatusCode(StatusCodes.Status501NotImplemented, "Content type generation is not yet available.");
        }
    }

    #endregion
}
