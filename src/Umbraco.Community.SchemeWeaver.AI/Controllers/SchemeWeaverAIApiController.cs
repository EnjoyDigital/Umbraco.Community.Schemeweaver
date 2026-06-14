using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Core;
using Umbraco.Community.SchemeWeaver.AI.Models;
using Umbraco.Community.SchemeWeaver.AI.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.AI.Controllers;

/// <summary>
/// Management API controller for SchemeWeaver AI-powered operations.
/// </summary>
[Route("umbraco/management/api/v1/schemeweaver/ai")]
[ApiExplorerSettings(GroupName = "SchemeWeaverAI")]
[MapToApi("management")]
[ApiController]
[Authorize(AuthenticationSchemes = Constants.Security.BackOfficeAuthenticationType)]
public class SchemeWeaverAIApiController : ControllerBase
{
    private readonly IAISchemaMapper _aiMapper;
    private readonly ILogger<SchemeWeaverAIApiController> _logger;

    public SchemeWeaverAIApiController(
        IAISchemaMapper aiMapper,
        ILogger<SchemeWeaverAIApiController> logger)
    {
        _aiMapper = aiMapper;
        _logger = logger;
    }

    /// <summary>
    /// Returns whether AI features are available. The presence of a 200 response
    /// indicates the SchemeWeaver.AI satellite package is installed.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        return Ok(new { available = true });
    }

    /// <summary>
    /// Uses AI to suggest Schema.org types for a single content type.
    /// </summary>
    [HttpPost("suggest-schema-type/{contentTypeAlias}")]
    [ProducesResponseType(typeof(SchemaTypeSuggestion[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> SuggestSchemaType(
        string contentTypeAlias, CancellationToken ct)
    {
        try
        {
            var suggestions = await _aiMapper.SuggestSchemaTypesAsync(contentTypeAlias, ct);
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI schema type suggestion failed for {ContentType}", contentTypeAlias);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "AI analysis failed. Please try again." });
        }
    }

    /// <summary>
    /// Uses AI to suggest Schema.org types for all content types in bulk.
    /// </summary>
    [HttpPost("suggest-schema-types-bulk")]
    [ProducesResponseType(typeof(BulkSchemaTypeSuggestion[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> SuggestSchemaTypesBulk(CancellationToken ct)
    {
        try
        {
            var suggestions = await _aiMapper.SuggestSchemaTypesForAllAsync(ct);
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI bulk schema type suggestion failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "AI bulk analysis failed. Please try again." });
        }
    }

    /// <summary>
    /// Uses AI to suggest property mappings between a content type and a Schema.org type.
    /// Falls back to heuristic mappings on AI failure.
    /// </summary>
    [HttpPost("ai-auto-map/{contentTypeAlias}")]
    [ProducesResponseType(typeof(PropertyMappingSuggestion[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> AIAutoMap(
        string contentTypeAlias,
        [FromQuery] string schemaTypeName,
        CancellationToken ct)
    {
        try
        {
            var suggestions = await _aiMapper.SuggestPropertyMappingsAsync(
                contentTypeAlias, schemaTypeName, ct);
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI auto-map failed for {ContentType}/{SchemaType}",
                contentTypeAlias, schemaTypeName);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "AI mapping failed. Please try again." });
        }
    }
}
