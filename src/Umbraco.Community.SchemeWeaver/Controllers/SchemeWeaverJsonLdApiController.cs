using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Controllers;

/// <summary>
/// Dedicated Delivery API endpoint returning the SchemeWeaver JSON-LD block array for a
/// content item. Separate from <c>/content/item/*</c> responses because Umbraco's builders
/// only surface real <see cref="Umbraco.Cms.Core.Models.PublishedContent.IPublishedProperty"/>
/// values — not synthetic index-handler fields. Consumers fetch this endpoint in parallel
/// with their content fetch and inject the strings as
/// <c>&lt;script type="application/ld+json"&gt;</c> tags.
/// </summary>
[ApiController]
[ApiVersion("2.0")]
[Route("umbraco/delivery/api/v{version:apiVersion}/schemeweaver")]
[JsonOptionsName(Constants.JsonOptionsNames.DeliveryApi)]
// `DeliveryApiConfiguration.ApiName` is `internal` in Umbraco core, so we hardcode the
// literal "delivery" — changing this would break the Delivery API versioning group.
[MapToApi("delivery")]
[ApiExplorerSettings(GroupName = "SchemeWeaver")]
public sealed class SchemeWeaverJsonLdApiController : ControllerBase
{
    private readonly IApiAccessService _apiAccessService;
    private readonly IRequestPreviewService _requestPreviewService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IApiContentPathResolver _apiContentPathResolver;
    private readonly IJsonLdBlocksProvider _blocksProvider;
    private readonly ILogger<SchemeWeaverJsonLdApiController> _logger;

    public SchemeWeaverJsonLdApiController(
        IApiAccessService apiAccessService,
        IRequestPreviewService requestPreviewService,
        IUmbracoContextAccessor umbracoContextAccessor,
        IApiContentPathResolver apiContentPathResolver,
        IJsonLdBlocksProvider blocksProvider,
        ILogger<SchemeWeaverJsonLdApiController> logger)
    {
        _apiAccessService = apiAccessService;
        _requestPreviewService = requestPreviewService;
        _umbracoContextAccessor = umbracoContextAccessor;
        _apiContentPathResolver = apiContentPathResolver;
        _blocksProvider = blocksProvider;
        _logger = logger;
    }

    /// <summary>
    /// Resolve the JSON-LD blocks by content key.
    /// </summary>
    [HttpGet("json-ld")]
    [MapToApiVersion("2.0")]
    [ProducesResponseType(typeof(SchemeWeaverJsonLdResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetById([FromQuery] Guid id, [FromQuery] string? culture = null)
    {
        if (!CheckAccess()) return Unauthorized();
        if (id == Guid.Empty) return BadRequest(new { error = "`id` must be a non-empty Guid." });

        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            _logger.LogWarning("UmbracoContext not available when serving /schemeweaver/json-ld");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        var content = umbracoContext.Content?.GetById(id);
        if (content is null) return NotFound();

        var blocks = _blocksProvider.GetBlocks(content, NormaliseCulture(culture));
        return Ok(new SchemeWeaverJsonLdResponse(blocks));
    }

    /// <summary>
    /// Resolve the JSON-LD blocks by route path (e.g. <c>/about-us</c>). Mirrors the lookup
    /// semantics of <c>/content/item/{*path}</c> so consumers that already have a route can
    /// skip the key-resolution round-trip.
    /// </summary>
    [HttpGet("json-ld/by-route")]
    [MapToApiVersion("2.0")]
    [ProducesResponseType(typeof(SchemeWeaverJsonLdResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetByRoute([FromQuery] string route, [FromQuery] string? culture = null)
    {
        if (!CheckAccess()) return Unauthorized();
        if (string.IsNullOrWhiteSpace(route))
        {
            return BadRequest(new { error = "`route` must be a non-empty string (e.g. `/about`)." });
        }

        var normalisedRoute = route.StartsWith('/') ? route : "/" + route;
        var content = _apiContentPathResolver.ResolveContentPath(normalisedRoute);
        if (content is null) return NotFound();

        var blocks = _blocksProvider.GetBlocks(content, NormaliseCulture(culture));
        return Ok(new SchemeWeaverJsonLdResponse(blocks));
    }

    private bool CheckAccess()
    {
        // Mirrors Umbraco's internal DeliveryApiAccessAttribute: preview requests need
        // preview access, everything else needs the public-access credential (Api-Key when
        // DeliveryApi:PublicAccess is false).
        return _requestPreviewService.IsPreview()
            ? _apiAccessService.HasPreviewAccess()
            : _apiAccessService.HasPublicAccess();
    }

    private static string? NormaliseCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return null;
        return culture.Trim();
    }
}
