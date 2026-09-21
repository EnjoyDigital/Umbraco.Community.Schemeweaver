using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe;

/// <summary>
/// Unit tests for <see cref="TypeSafeClient"/> over a fake <see cref="HttpMessageHandler"/>:
/// the wire format the eval harness proved (<c>eval/typesafe.mjs</c>), the retry policy and
/// the failure translation. No request ever leaves the process.
/// </summary>
public class TypeSafeClientTests
{
    private const string ApiKey = "test-key";

    private static readonly IReadOnlyDictionary<string, SystemOneQuestion> OneQuestion =
        new Dictionary<string, SystemOneQuestion>
        {
            ["q1"] = SystemOneQuestion.Choice("Pick one.", new Dictionary<string, string> { ["Headline"] = "The title", ["__none"] = "None" }),
        };

    private const string SuccessBody = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "q1": { "type": "choice", "choice": "Headline", "confidence": 0.91, "probabilities": { "Headline": 0.91, "__none": 0.09 } },
            "q2": { "type": "noul", "noul": 0.8 }
          },
          "usage": { "input_tokens": 123, "output_tokens": 4 }
        }
        """;

    // -----------------------------------------------------------------------
    // Infrastructure
    // -----------------------------------------------------------------------

    /// <summary>
    /// Replays a queue of responses (the last one repeats) and captures what each request
    /// carried. Bodies and headers are read INSIDE SendAsync because the client disposes the
    /// request message as soon as the call returns.
    /// </summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;

        public FakeHandler(params Func<HttpResponseMessage>[] responses)
            => _responses = new Queue<Func<HttpResponseMessage>>(responses);

        public List<string> Bodies { get; } = [];
        public List<Uri?> Uris { get; } = [];
        public List<AuthenticationHeaderValue?> Authorizations { get; } = [];
        public List<string?> ContentTypes { get; } = [];
        public int Calls => Bodies.Count;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            Uris.Add(request.RequestUri);
            Authorizations.Add(request.Headers.Authorization);
            ContentTypes.Add(request.Content.Headers.ContentType?.MediaType);

            var next = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return next();
        }
    }

    private static Func<HttpResponseMessage> Json(HttpStatusCode status, string body, TimeSpan? retryAfter = null)
        => () =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (retryAfter is { } delta)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
            return response;
        };

    private static Func<HttpResponseMessage> Throwing(Exception exception) => () => throw exception;

    private static TypeSafeClient CreateClient(
        FakeHandler handler,
        Action<TypeSafeOptions>? configure = null,
        RecordingLogger<TypeSafeClient>? logger = null)
    {
        var options = new TypeSafeOptions { ApiKey = ApiKey };
        configure?.Invoke(options);
        return new TypeSafeClient(new HttpClient(handler), Options.Create(options), logger ?? new RecordingLogger<TypeSafeClient>());
    }

    // -----------------------------------------------------------------------
    // Success path and wire format
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AskAsync_Success_ParsesAnswersAndUsage()
    {
        var handler = new FakeHandler(Json(HttpStatusCode.OK, SuccessBody));
        var client = CreateClient(handler);

        var response = await client.AskAsync("state", OneQuestion);

        response.Model.Should().Be("jev-1.13.0");
        response.Answers.Should().HaveCount(2);
        response.Answers["q1"].Type.Should().Be("choice");
        response.Answers["q1"].Choice.Should().Be("Headline");
        response.Answers["q1"].Confidence.Should().Be(0.91);
        response.Answers["q1"].Probabilities.Should().ContainKey("Headline").WhoseValue.Should().Be(0.91);
        response.Answers["q2"].Type.Should().Be("noul");
        response.Answers["q2"].Noul.Should().Be(0.8);
        response.Usage.Should().NotBeNull();
        response.Usage!.InputTokens.Should().Be(123);
        response.Usage.OutputTokens.Should().Be(4);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AskAsync_SendsBearerKeyJsonBodyAndEndpoint()
    {
        var handler = new FakeHandler(Json(HttpStatusCode.OK, SuccessBody));
        var client = CreateClient(handler, o => o.Model = "jev-pinned");

        var questions = new Dictionary<string, SystemOneQuestion>
        {
            ["bind"] = SystemOneQuestion.Choice("Which property?", new Dictionary<string, string> { ["Headline"] = "The title", ["__none"] = "None" }),
            ["entity"] = SystemOneQuestion.Noul("Is it an entity?", new NoulCriteria("Yes it is", "No it is not")),
            ["level"] = SystemOneQuestion.Score("How good?", ["poor", "fine", "great"]),
        };

        await client.AskAsync(new { umbracoContentType = new { alias = "blogPost" } }, questions);

        handler.Uris[0].Should().Be(new Uri("https://api.typesafe.ai/v1/systemone"));
        handler.Authorizations[0].Should().NotBeNull();
        handler.Authorizations[0]!.Scheme.Should().Be("Bearer");
        handler.Authorizations[0]!.Parameter.Should().Be(ApiKey);
        handler.ContentTypes[0].Should().Be("application/json");

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var root = body.RootElement;
        root.GetProperty("state").GetProperty("umbracoContentType").GetProperty("alias").GetString().Should().Be("blogPost");
        root.GetProperty("model").GetString().Should().Be("jev-pinned");

        var q = root.GetProperty("questions");
        q.GetProperty("bind").GetProperty("type").GetString().Should().Be("choice");
        q.GetProperty("bind").GetProperty("instructions").GetString().Should().Be("Which property?");
        q.GetProperty("bind").GetProperty("criteria").GetProperty("Headline").GetString().Should().Be("The title");
        q.GetProperty("bind").GetProperty("criteria").GetProperty("__none").GetString().Should().Be("None");

        // A Noul's criteria is the {"true":…,"false":…} object; a prose string is rejected with 422.
        q.GetProperty("entity").GetProperty("type").GetString().Should().Be("noul");
        q.GetProperty("entity").GetProperty("criteria").GetProperty("true").GetString().Should().Be("Yes it is");
        q.GetProperty("entity").GetProperty("criteria").GetProperty("false").GetString().Should().Be("No it is not");

        q.GetProperty("level").GetProperty("type").GetString().Should().Be("score");
        q.GetProperty("level").GetProperty("criteria").EnumerateArray().Select(e => e.GetString()).Should().Equal("poor", "fine", "great");
    }

    [Fact]
    public async Task AskAsync_NoulWithoutCriteria_OmitsCriteriaFromWire()
    {
        var handler = new FakeHandler(Json(HttpStatusCode.OK, SuccessBody));
        var client = CreateClient(handler);

        await client.AskAsync("ping", new Dictionary<string, SystemOneQuestion>
        {
            ["ping"] = SystemOneQuestion.Noul("Is the state exactly the word ping?"),
        });

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var ping = body.RootElement.GetProperty("questions").GetProperty("ping");
        ping.GetProperty("type").GetString().Should().Be("noul");
        ping.TryGetProperty("criteria", out _).Should().BeFalse("a null criteria must be left off the wire, not sent as null");
    }

    // -----------------------------------------------------------------------
    // Failure translation and retry policy
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AskAsync_422_ThrowsAfterExactlyOneCall()
    {
        var handler = new FakeHandler(Json(HttpStatusCode.UnprocessableEntity, """{"detail":"criteria must be an object"}"""));
        var client = CreateClient(handler, o => o.MaxRetries = 3);

        var act = () => client.AskAsync("state", OneQuestion);

        var ex = await act.Should().ThrowAsync<TypeSafeApiException>();
        ex.Which.StatusCode.Should().Be(422);
        ex.Which.Message.Should().Contain("criteria must be an object");
        handler.Calls.Should().Be(1, "a malformed request is never retried");
    }

    [Fact]
    public async Task AskAsync_429Then200_SucceedsWithExactlyTwoCalls()
    {
        var handler = new FakeHandler(
            Json(HttpStatusCode.TooManyRequests, "slow down", TimeSpan.FromMilliseconds(1)),
            Json(HttpStatusCode.OK, SuccessBody));
        var client = CreateClient(handler, o => o.MaxRetries = 1);

        var response = await client.AskAsync("state", OneQuestion);

        response.Answers.Should().ContainKey("q1");
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task AskAsync_529BeyondMaxRetries_Throws()
    {
        var handler = new FakeHandler(Json((HttpStatusCode)529, "overloaded", TimeSpan.FromMilliseconds(1)));
        var client = CreateClient(handler, o => o.MaxRetries = 1);

        var act = () => client.AskAsync("state", OneQuestion);

        var ex = await act.Should().ThrowAsync<TypeSafeApiException>();
        ex.Which.StatusCode.Should().Be(529);
        ex.Which.Message.Should().Contain("retries were exhausted");
        handler.Calls.Should().Be(2, "one attempt plus MaxRetries retries");
    }

    [Fact]
    public async Task AskAsync_RetryAfterHeader_IsHonouredOverExponentialBackoff()
    {
        // The default first backoff step is 500 ms; a 1 ms Retry-After (plus at most 250 ms of
        // jitter) must be what the client waits, which the retry log line reports exactly.
        var logger = new RecordingLogger<TypeSafeClient>();
        var handler = new FakeHandler(
            Json(HttpStatusCode.TooManyRequests, "slow down", TimeSpan.FromMilliseconds(1)),
            Json(HttpStatusCode.OK, SuccessBody));
        var client = CreateClient(handler, o => o.MaxRetries = 1, logger);

        await client.AskAsync("state", OneQuestion);

        var retry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("retrying in")).Subject;
        var delayMs = Convert.ToInt64(retry.Values["DelayMs"]);
        delayMs.Should().BeLessThan(300, "Retry-After of 1 ms plus jitter (< 250 ms) must win over the 500 ms backoff step");
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task AskAsync_HandlerThrowsHttpRequestException_SurfacesAsTypeSafeApiException()
    {
        var handler = new FakeHandler(Throwing(new HttpRequestException("connection refused")));
        var client = CreateClient(handler, o => o.MaxRetries = 2);

        var act = () => client.AskAsync("state", OneQuestion);

        var ex = await act.Should().ThrowAsync<TypeSafeApiException>();
        ex.Which.StatusCode.Should().BeNull("a transport error has no HTTP status");
        ex.Which.InnerException.Should().BeOfType<HttpRequestException>();
        ex.Which.Message.Should().Contain("connection refused");
        handler.Calls.Should().Be(1, "transport errors are not retried");
    }

    [Fact]
    public async Task AskAsync_NonJsonSuccessBody_ThrowsTypeSafeApiException()
    {
        var handler = new FakeHandler(Json(HttpStatusCode.OK, "<html>not json</html>"));
        var client = CreateClient(handler);

        var act = () => client.AskAsync("state", OneQuestion);

        var ex = await act.Should().ThrowAsync<TypeSafeApiException>();
        ex.Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task AskAsync_WithoutApiKey_RefusesBeforeSending()
    {
        var handler = new FakeHandler(Json(HttpStatusCode.OK, SuccessBody));
        var client = CreateClient(handler, o => o.ApiKey = null);

        var act = () => client.AskAsync("state", OneQuestion);

        await act.Should().ThrowAsync<TypeSafeApiException>().WithMessage("*ApiKey*");
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AskAsync_NoQuestions_ThrowsArgumentException()
    {
        var client = CreateClient(new FakeHandler(Json(HttpStatusCode.OK, SuccessBody)));

        var act = () => client.AskAsync("state", new Dictionary<string, SystemOneQuestion>());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // IsConfigured
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(true, "key", true)]
    [InlineData(true, null, false)]
    [InlineData(true, "   ", false)]
    [InlineData(false, "key", false)]
    [InlineData(false, null, false)]
    public void IsConfigured_RequiresEnabledAndKey(bool enabled, string? apiKey, bool expected)
    {
        var client = CreateClient(new FakeHandler(Json(HttpStatusCode.OK, SuccessBody)), o =>
        {
            o.Enabled = enabled;
            o.ApiKey = apiKey;
        });

        client.IsConfigured.Should().Be(expected);
    }
}
