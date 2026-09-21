namespace Umbraco.Community.SchemeWeaver.TypeSafe.Client;

/// <summary>
/// The TypeSafe System One API. One method: a shared state plus a map of independent
/// questions, evaluated in parallel, returning typed answers. There is no .NET SDK from
/// TypeSafe, so this is the package's own thin client over <c>POST /v1/systemone</c>.
/// </summary>
public interface ITypeSafeClient
{
    /// <summary>
    /// <c>true</c> when the satellite is enabled and has an API key. Callers check this
    /// before building questions so an unconfigured install costs nothing and falls
    /// through to the prior mapper silently (one startup log line aside).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends one request. Retries HTTP 429/529 with backoff per <c>TypeSafeOptions.MaxRetries</c>;
    /// any other failure throws <see cref="TypeSafeApiException"/>. Callers are expected to
    /// chunk large question sets themselves (see <c>TypeSafeOptions.MaxQuestionsPerRequest</c>)
    /// and to treat an exception as "use the fallback", never as a page-breaking error.
    /// </summary>
    Task<SystemOneResponse> AskAsync(
        object state,
        IReadOnlyDictionary<string, SystemOneQuestion> questions,
        CancellationToken cancellationToken = default);
}

/// <summary>A non-retryable or retries-exhausted failure talking to the TypeSafe API.</summary>
public sealed class TypeSafeApiException : Exception
{
    /// <summary>HTTP status code when the failure was an HTTP response; null for transport errors.</summary>
    public int? StatusCode { get; }

    public TypeSafeApiException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
        => StatusCode = statusCode;
}
