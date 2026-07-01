using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Umbraco.Community.SchemeWeaver.Controllers;

/// <summary>
/// Funnels any unhandled exception from a SchemeWeaver API action into the frozen wire contract:
/// HTTP 500 with a body of <c>{ error: "An unexpected error occurred whilst &lt;operation&gt;." }</c>.
/// The frontend (<c>schemeweaver.server-data-source.ts</c>) parses that exact shape, so this must
/// NOT be changed to ProblemDetails. Route values and the query string are logged so the
/// structured per-request parameters the old per-action catch blocks captured are preserved.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class HandlesServerErrorAttribute : ExceptionFilterAttribute
{
    private readonly string _operation;

    public HandlesServerErrorAttribute(string operation)
    {
        _operation = operation;
    }

    public override void OnException(ExceptionContext context)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILogger<SchemeWeaverApiController>>();

        logger.LogError(
            context.Exception,
            "SchemeWeaver API failure whilst {Operation} ({Action}; route {RouteValues}; query {QueryString})",
            _operation,
            context.ActionDescriptor.DisplayName,
            context.RouteData.Values,
            context.HttpContext.Request.QueryString);

        context.Result = new ObjectResult(new { error = $"An unexpected error occurred whilst {_operation}." })
        {
            StatusCode = StatusCodes.Status500InternalServerError,
        };
        context.ExceptionHandled = true;
    }
}
