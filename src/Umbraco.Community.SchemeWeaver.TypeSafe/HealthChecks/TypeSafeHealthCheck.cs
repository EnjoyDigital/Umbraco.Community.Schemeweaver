using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.HealthChecks;

/// <summary>
/// Backoffice health check for the TypeSafe satellite (Settings, Health Check, group
/// "SchemeWeaver"). Reports whether the satellite is enabled, whether it has an API key,
/// and, when it has one, whether the TypeSafe System One API answers: one tiny real request
/// (a single Noul over the state <c>ping</c>) inside a ten second window, reporting the
/// model id and the round trip.
/// </summary>
/// <remarks>
/// No remediation actions: the only fixes are configuration (a key via user-secrets or an
/// environment variable) and network reachability, neither of which a button in the
/// backoffice should attempt. The ping is deliberately the cheapest possible question so
/// the check costs a fraction of a cent however often the dashboard is refreshed.
/// </remarks>
[HealthCheck(
    "5b3c8e2a-1f4d-4c6b-9a7e-2d0f6c8b4e91",
    "SchemeWeaver TypeSafe",
    Description = "Checks whether the SchemeWeaver TypeSafe satellite is enabled, has an API key and can reach the TypeSafe System One API.",
    Group = "SchemeWeaver")]
public sealed class TypeSafeHealthCheck : HealthCheck
{
    /// <summary>Upper bound on the ping; Jev answers in about 100 ms, so anything near this is a network problem.</summary>
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(10);

    private const string PingQuestionId = "ping";

    private readonly ITypeSafeClient _client;
    private readonly IOptions<TypeSafeOptions> _options;
    private readonly ILogger<TypeSafeHealthCheck> _logger;

    public TypeSafeHealthCheck(
        ITypeSafeClient client,
        IOptions<TypeSafeOptions> options,
        ILogger<TypeSafeHealthCheck> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
        => [await CheckAsync().ConfigureAwait(false)];

    /// <inheritdoc />
    public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
        => new("SchemeWeaver TypeSafe has no actions to execute.") { ResultType = StatusResultType.Info };

    private async Task<HealthCheckStatus> CheckAsync()
    {
        TypeSafeOptions options;
        try
        {
            options = _options.Value;
        }
        catch (OptionsValidationException ex)
        {
            return new HealthCheckStatus($"SchemeWeaver TypeSafe configuration is invalid: {string.Join(" ", ex.Failures)}")
            {
                ResultType = StatusResultType.Error,
            };
        }

        if (!options.Enabled)
        {
            return new HealthCheckStatus(
                "SchemeWeaver TypeSafe is disabled (SchemeWeaver:TypeSafe:Enabled=false); auto-map uses the prior mapper.")
            {
                ResultType = StatusResultType.Info,
            };
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return new HealthCheckStatus(
                "SchemeWeaver TypeSafe is not configured (set SchemeWeaver:TypeSafe:ApiKey via user-secrets or the SchemeWeaver__TypeSafe__ApiKey environment variable); auto-map uses the prior mapper.")
            {
                ResultType = StatusResultType.Warning,
            };
        }

        try
        {
            using var window = new CancellationTokenSource(PingTimeout);
            var questions = new Dictionary<string, SystemOneQuestion>
            {
                [PingQuestionId] = SystemOneQuestion.Noul(
                    "Is the state exactly the word \"ping\"?",
                    new NoulCriteria(
                        "The state is the single word ping.",
                        "The state is anything other than the word ping.")),
            };

            var stopwatch = Stopwatch.StartNew();
            var response = await _client.AskAsync("ping", questions, window.Token).ConfigureAwait(false);
            stopwatch.Stop();

            var probability = response.Answers.TryGetValue(PingQuestionId, out var answer) ? answer.Noul : null;
            var model = string.IsNullOrWhiteSpace(response.Model) ? options.Model : response.Model;

            return new HealthCheckStatus(
                $"SchemeWeaver TypeSafe is reachable: model {model} answered in {stopwatch.ElapsedMilliseconds} ms.")
            {
                ResultType = StatusResultType.Success,
                Description = probability is { } p
                    ? $"One Noul over the state \"ping\" returned p(true) = {p:0.00}; {response.Usage?.InputTokens ?? 0} input tokens."
                    : "The request succeeded but the ping answer was missing from the response.",
            };
        }
        catch (TypeSafeApiException ex)
        {
            var status = ex.StatusCode is { } code ? $" (HTTP {code})" : string.Empty;
            return new HealthCheckStatus($"SchemeWeaver TypeSafe request failed{status}: {ex.Message}")
            {
                ResultType = StatusResultType.Error,
            };
        }
        catch (OperationCanceledException)
        {
            return new HealthCheckStatus(
                $"SchemeWeaver TypeSafe did not answer within {PingTimeout.TotalSeconds:0} seconds.")
            {
                ResultType = StatusResultType.Error,
            };
        }
        catch (Exception ex)
        {
            // By policy: a health check must report, never throw into the dashboard.
            _logger.LogWarning(ex, "SchemeWeaver TypeSafe: health check ping failed unexpectedly.");
            return new HealthCheckStatus($"SchemeWeaver TypeSafe health check failed: {ex.Message}")
            {
                ResultType = StatusResultType.Error,
            };
        }
    }
}
