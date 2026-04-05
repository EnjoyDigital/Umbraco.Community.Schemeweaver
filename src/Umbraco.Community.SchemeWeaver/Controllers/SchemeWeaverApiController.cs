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
        try
        {
            var types = string.IsNullOrWhiteSpace(search)
                ? _service.GetSchemaTypes()
                : _service.SearchSchemaTypes(search);

            return Ok(types);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve schema types");
            return StatusCode(500, new { error = "An unexpected error occurred whilst retrieving schema types." });
        }
    }

    [HttpGet("schema-types/{name}/properties")]
    [ProducesResponseType(typeof(IEnumerable<SchemaPropertyInfo>), StatusCodes.Status200OK)]
    public IActionResult GetSchemaTypeProperties(string name)
    {
        try
        {
            var properties = _service.GetSchemaProperties(name);
            return Ok(properties);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve properties for schema type {SchemaTypeName}", name);
            return StatusCode(500, new { error = "An unexpected error occurred whilst retrieving schema type properties." });
        }
    }

    #endregion

    #region Content Types

    [HttpGet("content-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetContentTypes()
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve content types");
            return StatusCode(500, new { error = "An unexpected error occurred whilst retrieving content types." });
        }
    }

    [HttpGet("content-types/{alias}/properties")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetContentTypeProperties(string alias)
    {
        try
        {
            var contentType = _contentTypeService.Get(alias);
            if (contentType == null) return NotFound();

            var customProperties = contentType.PropertyTypes.Select(pt => new
            {
                pt.Alias,
                pt.Name,
                EditorAlias = pt.PropertyEditorAlias,
                pt.Description
            });

            var builtInProperties = SchemeWeaverConstants.BuiltInProperties.All.Select(bp => new
            {
                Alias = bp.Alias,
                Name = bp.DisplayName,
                EditorAlias = bp.EditorAlias,
                Description = (string?)null
            });

            return Ok(builtInProperties.Concat(customProperties));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve properties for content type {ContentTypeAlias}", alias);
            return StatusCode(500, new { error = "An unexpected error occurred whilst retrieving content type properties." });
        }
    }

    [HttpGet("content-types/{contentTypeAlias}/properties/{propertyAlias}/block-types")]
    [ProducesResponseType(typeof(IEnumerable<BlockElementTypeInfo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlockElementTypes(string contentTypeAlias, string propertyAlias)
    {
        try
        {
            var blockTypes = await _service.GetBlockElementTypesAsync(contentTypeAlias, propertyAlias).ConfigureAwait(false);
            return Ok(blockTypes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve block element types for {ContentTypeAlias}/{PropertyAlias}", contentTypeAlias, propertyAlias);
            return StatusCode(500, new { error = "An unexpected error occurred whilst retrieving block element types." });
        }
    }

    #endregion

    #region Mappings

    [HttpGet("mappings")]
    [ProducesResponseType(typeof(IEnumerable<SchemaMappingDto>), StatusCodes.Status200OK)]
    public IActionResult GetMappings()
    {
        try
        {
            return Ok(_service.GetAllMappings());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve schema mappings");
            return StatusCode(500, new { error = "An unexpected error occurred whilst retrieving schema mappings." });
        }
    }

    [HttpGet("mappings/{contentTypeAlias}")]
    [ProducesResponseType(typeof(SchemaMappingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetMapping(string contentTypeAlias)
    {
        try
        {
            var mapping = _service.GetMapping(contentTypeAlias);
            if (mapping == null) return NotFound();
            return Ok(mapping);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve mapping for content type {ContentTypeAlias}", contentTypeAlias);
            return StatusCode(500, new { error = "An unexpected error occurred whilst retrieving the schema mapping." });
        }
    }

    [HttpPost("mappings")]
    [ProducesResponseType(typeof(SchemaMappingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult SaveMapping([FromBody] SchemaMappingDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.ContentTypeAlias))
                return BadRequest("ContentTypeAlias is required.");

            if (string.IsNullOrWhiteSpace(dto.SchemaTypeName))
                return BadRequest("SchemaTypeName is required.");

            var saved = _service.SaveMapping(dto);
            return Ok(saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save mapping for content type {ContentTypeAlias}", dto.ContentTypeAlias);
            return StatusCode(500, new { error = "An unexpected error occurred whilst saving the schema mapping." });
        }
    }

    [HttpDelete("mappings/{contentTypeAlias}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult DeleteMapping(string contentTypeAlias)
    {
        try
        {
            _service.DeleteMapping(contentTypeAlias);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete mapping for content type {ContentTypeAlias}", contentTypeAlias);
            return StatusCode(500, new { error = "An unexpected error occurred whilst deleting the schema mapping." });
        }
    }

    [HttpPost("mappings/{contentTypeAlias}/auto-map")]
    [ProducesResponseType(typeof(IEnumerable<PropertyMappingSuggestion>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult AutoMap(string contentTypeAlias, [FromQuery] string schemaTypeName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(schemaTypeName))
                return BadRequest("schemaTypeName query parameter is required.");

            var suggestions = _service.AutoMap(contentTypeAlias, schemaTypeName);
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-map {ContentTypeAlias} to {SchemaTypeName}", contentTypeAlias, schemaTypeName);
            return StatusCode(500, new { error = "An unexpected error occurred whilst generating auto-map suggestions." });
        }
    }

    [HttpPost("mappings/{contentTypeAlias}/preview")]
    [ProducesResponseType(typeof(JsonLdPreviewResponse), StatusCodes.Status200OK)]
    public IActionResult Preview(string contentTypeAlias, [FromQuery] Guid? contentKey = null)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate preview for content type {ContentTypeAlias}", contentTypeAlias);
            return StatusCode(500, new { error = "An unexpected error occurred whilst generating the JSON-LD preview." });
        }
    }

    #endregion

    #region Content Type Generation (Phase 2)

    [HttpPost("generate-content-type")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateContentType([FromBody] ContentTypeGenerationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SchemaTypeName))
                return BadRequest("SchemaTypeName is required.");

            if (string.IsNullOrWhiteSpace(request.DocumentTypeName))
                return BadRequest("DocumentTypeName is required.");

            var key = await _contentTypeGenerator.GenerateContentTypeAsync(request, cancellationToken).ConfigureAwait(false);
            return Ok(new { Key = key });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate content type from schema {SchemaTypeName}", request.SchemaTypeName);
            return StatusCode(500, new { error = "An unexpected error occurred whilst generating the content type." });
        }
    }

    #endregion
}
