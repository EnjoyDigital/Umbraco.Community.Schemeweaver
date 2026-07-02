using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Community.SchemeWeaver.Controllers;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Controllers;

/// <summary>
/// Pins the frozen wire contract of <see cref="HandlesServerErrorAttribute"/>: HTTP 500 with a
/// body that serialises to exactly <c>{"error":"An unexpected error occurred whilst …."}</c>
/// (the frontend data source parses this shape — it must never become ProblemDetails), the
/// exception marked handled, and the original exception logged at Error level.
/// </summary>
public class HandlesServerErrorAttributeTests
{
    private readonly ILogger<SchemeWeaverApiController> _logger =
        Substitute.For<ILogger<SchemeWeaverApiController>>();

    private ExceptionContext CreateExceptionContext(Exception exception)
    {
        var services = new ServiceCollection()
            .AddSingleton(_logger)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        var routeData = new RouteData();
        routeData.Values["contentTypeAlias"] = "article";

        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        return new ExceptionContext(actionContext, [])
        {
            Exception = exception,
        };
    }

    [Fact]
    public void OnException_Returns500WithFrozenErrorBody()
    {
        var sut = new HandlesServerErrorAttribute("testing");
        var context = CreateExceptionContext(new InvalidOperationException("boom"));

        sut.OnException(context);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        // The exact body shape is the wire contract the frontend parses — frozen.
        JsonSerializer.Serialize(result.Value)
            .Should().Be("""{"error":"An unexpected error occurred whilst testing."}""");
    }

    [Fact]
    public void OnException_MarksExceptionHandled()
    {
        var sut = new HandlesServerErrorAttribute("testing");
        var context = CreateExceptionContext(new InvalidOperationException("boom"));

        sut.OnException(context);

        context.ExceptionHandled.Should().BeTrue();
    }

    [Fact]
    public void OnException_LogsTheOriginalExceptionAtErrorLevel()
    {
        var exception = new InvalidOperationException("boom");
        var sut = new HandlesServerErrorAttribute("testing");
        var context = CreateExceptionContext(exception);

        sut.OnException(context);

        // ILogger.Log<TState> is generic, so inspect the received call rather than
        // matching the TState argument type directly.
        var logCall = _logger.ReceivedCalls()
            .Should().ContainSingle(c => c.GetMethodInfo().Name == nameof(ILogger.Log)).Subject;
        var args = logCall.GetArguments();
        args[0].Should().Be(LogLevel.Error);
        args[3].Should().BeSameAs(exception);
    }
}
