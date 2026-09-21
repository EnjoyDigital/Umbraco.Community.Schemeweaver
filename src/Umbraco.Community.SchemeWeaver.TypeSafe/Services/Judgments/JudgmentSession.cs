using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

/// <summary>
/// One mapper call's worth of System One requests: chunks a question map per
/// <c>TypeSafeOptions.MaxQuestionsPerRequest</c>, merges the answers, and keeps the token
/// and request tallies the Information log line reports. Also owns the tolerant answer
/// readers — a missing or malformed individual answer is never an error, it is
/// <c>__none</c> (the harness's <c>null</c> mode proved the pipeline degrades to an empty
/// mapping without crashing, and this class is where that guarantee lives).
/// </summary>
internal sealed class JudgmentSession
{
    /// <summary>The "none of these" option every binding question carries.</summary>
    public const string None = "__none";

    /// <summary>The "stay at this type" option every descent question carries.</summary>
    public const string Stop = "__stop";

    private static readonly Regex Unsafe = new("[^A-Za-z0-9_]", RegexOptions.Compiled);

    private readonly ITypeSafeClient _client;
    private readonly int _chunkSize;
    private readonly ILogger _logger;

    public JudgmentSession(ITypeSafeClient client, int chunkSize, ILogger logger)
    {
        _client = client;
        _chunkSize = Math.Max(1, chunkSize);
        _logger = logger;
    }

    /// <summary>Input tokens billed across every request so far.</summary>
    public long InputTokens { get; private set; }

    /// <summary>Requests sent so far.</summary>
    public int Requests { get; private set; }

    /// <summary>Questions asked so far.</summary>
    public int Questions { get; private set; }

    /// <summary>
    /// A question id from its parts, sanitised to <c>[A-Za-z0-9_]</c>. Ids are never sent to
    /// the model, so readability only matters for logs and tests.
    /// </summary>
    public static string Id(params string[] parts)
        => Unsafe.Replace(string.Join("__", parts), "_");

    /// <summary>
    /// Sends <paramref name="questions"/> in chunks that all share <paramref name="state"/>,
    /// sequentially (the harness measured no latency pressure and sequential stays well
    /// inside the rate limits). Throws <see cref="TypeSafeApiException"/> on a failed request.
    /// </summary>
    public async Task<Dictionary<string, SystemOneAnswer>> AskChunkedAsync(
        object state,
        IReadOnlyDictionary<string, SystemOneQuestion> questions,
        string round,
        CancellationToken cancellationToken)
    {
        var answers = new Dictionary<string, SystemOneAnswer>(StringComparer.Ordinal);
        if (questions.Count == 0)
            return answers;

        _logger.LogDebug("TypeSafe round {Round}: {QuestionCount} question(s) in chunks of {ChunkSize}",
            round, questions.Count, _chunkSize);

        var ids = questions.Keys.ToList();
        for (var i = 0; i < ids.Count; i += _chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var slice = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
            foreach (var id in ids.Skip(i).Take(_chunkSize))
                slice[id] = questions[id];

            var response = await _client.AskAsync(state, slice, cancellationToken).ConfigureAwait(false);

            Requests++;
            Questions += slice.Count;
            InputTokens += response.Usage?.InputTokens ?? 0;

            foreach (var (id, answer) in response.Answers)
            {
                if (answer is not null)
                    answers[id] = answer;
            }
        }

        return answers;
    }

    /// <summary>
    /// The chosen option of a Choice answer when it is one of <paramref name="allowed"/>;
    /// <c>null</c> for a missing answer, a malformed one, an option the question never
    /// offered, or <see cref="None"/>. The model can only select from what code supplied,
    /// but the wire is still untrusted input.
    /// </summary>
    public static string? Choice(
        IReadOnlyDictionary<string, SystemOneAnswer> answers,
        string id,
        IReadOnlyCollection<string> allowed)
    {
        if (!answers.TryGetValue(id, out var answer) || string.IsNullOrEmpty(answer.Choice))
            return null;

        if (string.Equals(answer.Choice, None, StringComparison.Ordinal))
            return null;

        return allowed.FirstOrDefault(o => string.Equals(o, answer.Choice, StringComparison.Ordinal));
    }

    /// <summary>
    /// Calibrated confidence of a Choice answer in the option it selected: the answer's
    /// <c>confidence</c>, else that option's entry in <c>probabilities</c>, else 0.
    /// </summary>
    public static double Confidence(IReadOnlyDictionary<string, SystemOneAnswer> answers, string id, string choice)
    {
        if (!answers.TryGetValue(id, out var answer))
            return 0;

        if (answer.Confidence is { } confidence && IsProbability(confidence))
            return confidence;

        if (answer.Probabilities is not null
            && answer.Probabilities.TryGetValue(choice, out var p)
            && IsProbability(p))
            return p;

        return 0;
    }

    /// <summary>The probability of a Noul answer, or 0 when missing or malformed.</summary>
    public static double Noul(IReadOnlyDictionary<string, SystemOneAnswer> answers, string id)
        => answers.TryGetValue(id, out var answer) && answer.Noul is { } p && IsProbability(p) ? p : 0;

    /// <summary>
    /// The option distribution of a Choice answer, as the descent consumes it: the reported
    /// probabilities when present, else the selected option at its confidence (or 1 when no
    /// confidence came back). Empty for a missing or malformed answer. Only finite values in
    /// (0, 1] survive.
    /// </summary>
    public static IReadOnlyList<(string Option, double Probability)> Distribution(
        IReadOnlyDictionary<string, SystemOneAnswer> answers,
        string id)
    {
        if (!answers.TryGetValue(id, out var answer))
            return [];

        var result = new List<(string, double)>();
        if (answer.Probabilities is { Count: > 0 })
        {
            foreach (var (option, p) in answer.Probabilities)
            {
                if (!string.IsNullOrEmpty(option) && IsProbability(p) && p > 0)
                    result.Add((option, p));
            }

            return result;
        }

        if (!string.IsNullOrEmpty(answer.Choice))
        {
            var p = answer.Confidence is { } c && IsProbability(c) && c > 0 ? c : 1;
            result.Add((answer.Choice, p));
        }

        return result;
    }

    private static bool IsProbability(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value is >= 0 and <= 1;
}
