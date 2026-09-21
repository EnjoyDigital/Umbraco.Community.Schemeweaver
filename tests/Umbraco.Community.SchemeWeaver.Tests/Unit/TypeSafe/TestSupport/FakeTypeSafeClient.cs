using Umbraco.Community.SchemeWeaver.TypeSafe.Client;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;

/// <summary>
/// An <see cref="ITypeSafeClient"/> that answers every question from a scripted delegate and
/// records every request it saw (state and questions), so tests can both drive the judgment
/// pipeline deterministically and assert on what the model was asked. Never touches the network.
/// </summary>
internal sealed class FakeTypeSafeClient : ITypeSafeClient
{
    private readonly Func<string, SystemOneQuestion, SystemOneAnswer?> _answer;

    /// <param name="answer">
    /// Answers one question by id; returning <c>null</c> leaves that id out of the response, which
    /// is how a "missing answer" is simulated.
    /// </param>
    public FakeTypeSafeClient(Func<string, SystemOneQuestion, SystemOneAnswer?> answer)
        => _answer = answer;

    /// <summary>Every request, in order: the shared state and the question map that went with it.</summary>
    public List<(object State, IReadOnlyDictionary<string, SystemOneQuestion> Questions)> Requests { get; } = [];

    /// <summary>Every question id asked so far, across all requests, in order.</summary>
    public IReadOnlyList<string> AskedIds => Requests.SelectMany(r => r.Questions.Keys).ToList();

    /// <summary>Every question asked so far, keyed by id (later duplicates win).</summary>
    public IReadOnlyDictionary<string, SystemOneQuestion> AskedQuestions
        => Requests.SelectMany(r => r.Questions)
            .GroupBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);

    /// <summary>When set, <see cref="AskAsync"/> throws this instead of answering.</summary>
    public Exception? Throws { get; set; }

    public bool IsConfigured { get; set; } = true;

    public Task<SystemOneResponse> AskAsync(
        object state,
        IReadOnlyDictionary<string, SystemOneQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        Requests.Add((state, new Dictionary<string, SystemOneQuestion>(questions, StringComparer.Ordinal)));

        if (Throws is not null)
            throw Throws;

        var answers = new Dictionary<string, SystemOneAnswer>(StringComparer.Ordinal);
        foreach (var (id, question) in questions)
        {
            var answer = _answer(id, question);
            if (answer is not null)
                answers[id] = answer;
        }

        return Task.FromResult(new SystemOneResponse
        {
            Model = "fake-jev",
            Answers = answers,
            Usage = new SystemOneUsage { InputTokens = 10 * questions.Count, OutputTokens = questions.Count },
        });
    }

    /// <summary>A Choice answer at the given confidence, with no per-option distribution.</summary>
    public static SystemOneAnswer Choice(string choice, double confidence = 0.95)
        => new() { Type = "choice", Choice = choice, Confidence = confidence, Probabilities = new Dictionary<string, double>() };

    /// <summary>A Choice answer with an explicit option distribution (the beam search consumes this).</summary>
    public static SystemOneAnswer Distribution(IReadOnlyDictionary<string, double> probabilities)
    {
        var top = probabilities.OrderByDescending(kv => kv.Value).First();
        return new SystemOneAnswer { Type = "choice", Choice = top.Key, Confidence = top.Value, Probabilities = probabilities };
    }

    public static SystemOneAnswer Noul(double probability)
        => new() { Type = "noul", Noul = probability };

    /// <summary>The option map of a Choice question (the mapper always passes a string dictionary).</summary>
    public static IReadOnlyDictionary<string, string> CriteriaOf(SystemOneQuestion question)
        => question.Criteria as IReadOnlyDictionary<string, string>
           ?? throw new InvalidOperationException($"Question of type '{question.Type}' has no option map.");

    /// <summary>The option key of a Choice question matching <paramref name="name"/> case-insensitively, or null.</summary>
    public static string? OptionNamed(SystemOneQuestion question, string? name)
    {
        if (string.IsNullOrEmpty(name) || question.Criteria is not IReadOnlyDictionary<string, string> criteria)
            return null;

        return criteria.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The type a beam-search level question is asked from, parsed out of its instructions.</summary>
    public static string? CurrentTypeOf(SystemOneQuestion question)
    {
        var text = question.Instructions as string ?? string.Empty;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"It is an? (\w+), and ");
        return match.Success ? match.Groups[1].Value : null;
    }
}
