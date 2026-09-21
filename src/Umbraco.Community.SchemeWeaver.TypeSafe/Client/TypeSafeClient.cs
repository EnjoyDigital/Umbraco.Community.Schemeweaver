using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Client;

/// <summary>
/// The package's own thin HTTP client for TypeSafe System One (<c>POST /v1/systemone</c>).
/// TypeSafe ships no .NET SDK, so this speaks the raw wire format the eval harness proved
/// (<c>eval/typesafe.mjs</c>): one JSON body of <c>state</c>, <c>model</c> and a map of
/// <c>questions</c>, a Bearer key, and a response of typed <c>answers</c> plus token usage.
/// </summary>
/// <remarks>
/// <para>
/// The retry policy mirrors the harness and the API's own guidance: only HTTP 429 (rate
/// limit) and 529 (overloaded) are retried, with exponential backoff plus jitter, and a
/// <c>Retry-After</c> header is honoured when the server sends one. Every other failure
/// throws <see cref="TypeSafeApiException"/> at once. A 422 in particular means the request
/// shape is wrong (the eval hit it when a Noul's criteria went out as a prose string instead
/// of a <see cref="NoulCriteria"/> object) and retrying a malformed request only burns time.
/// </para>
/// <para>
/// The client never logs the API key or the state. The state carries the customer's content
/// type structure, which does not belong in log files; Debug lines carry counts and timings.
/// </para>
/// <para>
/// Registered through <c>IHttpClientFactory</c> as a typed client, so each resolution gets a
/// fresh <see cref="HttpClient"/> over a pooled handler with its base address and timeout set
/// from <see cref="TypeSafeOptions"/> by the composer. The same options are applied here as
/// well, so a hand-built client (unit tests, a fake handler) behaves identically.
/// </para>
/// </remarks>
public sealed class TypeSafeClient : ITypeSafeClient
{
    /// <summary>First backoff step; doubles per attempt up to <see cref="MaxBackoff"/>, the schedule the eval harness ran.</summary>
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMilliseconds(500);

    /// <summary>Ceiling on the computed backoff before jitter.</summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Ceiling on a <c>Retry-After</c> wait. The header is honoured, but an auto-map request is
    /// a person waiting on a backoffice modal, so a server asking for minutes is treated as
    /// "wait this long, then give up" rather than stalling the request indefinitely.
    /// </summary>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    /// <summary>Random jitter added to every wait so parallel callers do not retry in lock-step.</summary>
    private const int JitterMilliseconds = 250;

    /// <summary>How much of an error body goes into the exception message and the log.</summary>
    private const int ErrorBodyExcerptLength = 400;

    /// <summary>
    /// Property names are explicit on the DTOs (<c>input_tokens</c>, <c>true</c>/<c>false</c>), so
    /// there is deliberately no naming policy. The encoder leaves non-ASCII text (content type
    /// names, editor descriptions) unescaped: input tokens are what TypeSafe bills, and a
    /// <c>é</c> escape costs more of them than the character it stands for. It is the same
    /// encoder the core JSON-LD generator uses, so it is not a new pattern to review.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly HttpClient _httpClient;
    private readonly TypeSafeOptions _options;
    private readonly ILogger<TypeSafeClient> _logger;
    private readonly Uri _endpoint;

    public TypeSafeClient(
        HttpClient httpClient,
        IOptions<TypeSafeOptions> options,
        ILogger<TypeSafeClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        // The factory sets BaseAddress from options; a hand-built HttpClient has none, so the
        // options endpoint is the fallback. Requests always carry this absolute URI, which makes
        // the two construction paths indistinguishable to the server and to tests.
        _endpoint = httpClient.BaseAddress ?? new Uri(_options.Endpoint, UriKind.Absolute);
    }

    /// <inheritdoc />
    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ApiKey);

    /// <inheritdoc />
    public async Task<SystemOneResponse> AskAsync(
        object state,
        IReadOnlyDictionary<string, SystemOneQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(questions);

        if (questions.Count == 0)
        {
            throw new ArgumentException("At least one question is required.", nameof(questions));
        }

        // Without a key there is nothing to authenticate with; refusing here (rather than
        // sending an anonymous request that fails with a 401) keeps the failure local and
        // keeps callers honest about checking IsConfigured first.
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new TypeSafeApiException("TypeSafe is not configured: SchemeWeaver:TypeSafe:ApiKey is not set.");
        }

        var request = new SystemOneRequest
        {
            State = state,
            Model = _options.Model,
            Questions = questions,
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(request, SerializerOptions);

        var stopwatch = Stopwatch.StartNew();
        for (var attempt = 0; ; attempt++)
        {
            using var response = await SendAsync(body, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                var parsed = await ReadResponseAsync(response, status, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug(
                    "SchemeWeaver TypeSafe: {QuestionCount} question(s) answered by {Model} in {ElapsedMs} ms ({InputTokens} input tokens, {Attempts} attempt(s)).",
                    questions.Count,
                    parsed.Model,
                    stopwatch.ElapsedMilliseconds,
                    parsed.Usage?.InputTokens ?? 0,
                    attempt + 1);
                return parsed;
            }

            var excerpt = await ReadErrorExcerptAsync(response, cancellationToken).ConfigureAwait(false);

            if (IsRetryable(status) && attempt < _options.MaxRetries)
            {
                var delay = RetryDelay(attempt, response.Headers.RetryAfter);
                _logger.LogWarning(
                    "SchemeWeaver TypeSafe: HTTP {StatusCode} on attempt {Attempt} of {MaxAttempts}; retrying in {DelayMs} ms.",
                    status,
                    attempt + 1,
                    _options.MaxRetries + 1,
                    (long)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var message = status switch
            {
                422 => $"TypeSafe rejected the request shape (HTTP 422); this is a question-building bug, not a transient failure: {excerpt}",
                _ when IsRetryable(status) => $"TypeSafe returned HTTP {status} and {_options.MaxRetries} retries were exhausted: {excerpt}",
                _ => $"TypeSafe returned HTTP {status}: {excerpt}",
            };
            _logger.LogWarning(
                "SchemeWeaver TypeSafe: request failed after {Attempts} attempt(s) in {ElapsedMs} ms: {Message}",
                attempt + 1,
                stopwatch.ElapsedMilliseconds,
                message);
            throw new TypeSafeApiException(message, status);
        }
    }

    /// <summary>Only rate limiting and overload are worth a retry; the eval harness used the same two codes.</summary>
    private static bool IsRetryable(int status) => status is 429 or 529;

    private async Task<HttpResponseMessage> SendAsync(byte[] body, CancellationToken cancellationToken)
    {
        // Per-attempt timeout from options, independent of HttpClient.Timeout, so a hand-built
        // client is guarded the same way as the factory-built one. Each retry gets a full window.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.Timeout > TimeSpan.Zero)
        {
            timeout.CancelAfter(_options.Timeout);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        try
        {
            return await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up (request aborted, health check window closed): that is not a
            // TypeSafe failure and must surface as the cancellation it is.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new TypeSafeApiException(
                $"TypeSafe did not answer within {_options.Timeout.TotalSeconds:0.#} s.",
                null,
                ex);
        }
        catch (HttpRequestException ex)
        {
            var statusCode = ex.StatusCode is { } code ? (int?)code : null;
            throw new TypeSafeApiException($"TypeSafe request failed: {ex.Message}", statusCode, ex);
        }
    }

    private static async Task<SystemOneResponse> ReadResponseAsync(
        HttpResponseMessage response,
        int status,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var parsed = await JsonSerializer
                .DeserializeAsync<SystemOneResponse>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            return parsed ?? throw new TypeSafeApiException("TypeSafe returned an empty response body.", status);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            throw new TypeSafeApiException(
                "TypeSafe returned a response that could not be read as a System One answer set.",
                status,
                ex);
        }
    }

    /// <summary>
    /// The first <see cref="ErrorBodyExcerptLength"/> characters of an error body, for the
    /// exception message. A 422 body names the offending field, which is the whole point;
    /// a body that cannot be read is simply left out rather than masking the status.
    /// </summary>
    private static async Task<string> ReadErrorExcerptAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
            return text.Length <= ErrorBodyExcerptLength ? text : text[..ErrorBodyExcerptLength];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// <c>Retry-After</c> when the server sent one (delta seconds or an HTTP date, capped at
    /// <see cref="MaxRetryAfter"/>), otherwise 500 ms doubled per attempt and capped at 8 s;
    /// both plus jitter.
    /// </summary>
    private static TimeSpan RetryDelay(int attempt, RetryConditionHeaderValue? retryAfter)
    {
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, JitterMilliseconds));

        if (retryAfter is not null)
        {
            TimeSpan? requested = retryAfter.Delta
                ?? (retryAfter.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            if (requested is { } wait && wait > TimeSpan.Zero)
            {
                return (wait < MaxRetryAfter ? wait : MaxRetryAfter) + jitter;
            }
        }

        // 500 ms x 2^attempt: attempt is bounded by MaxRetries (validated at 0-10), so the
        // shift cannot overflow, and the cap makes the tail flat at 8 s.
        var exponential = TimeSpan.FromMilliseconds(BaseBackoff.TotalMilliseconds * (1 << Math.Min(attempt, 10)));
        return (exponential < MaxBackoff ? exponential : MaxBackoff) + jitter;
    }
}
