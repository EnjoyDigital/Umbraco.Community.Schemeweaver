using Microsoft.Extensions.Logging;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

/// <summary>
/// Hierarchical classification down the Schema.org type tree: from a root type, each level
/// is one Choice over the current type's direct subtypes plus <c>__stop</c>, and the best
/// <c>K</c> paths stay alive. Shared by the nested-type descent (from a property's declared
/// range) and the schema-type suggester (from <c>Thing</c>).
/// </summary>
/// <remarks>
/// <para>
/// BEAM SEARCH, NOT GREEDY. A greedy walk commits to the most probable child at every level
/// and cannot recover from an early wrong turn. The eval harness measured exactly the
/// instability the hierarchical-classification cookbook warns about: tightening the stop
/// rule fixed <c>Product.review</c> (which had over-specified to <c>UserReview</c>) and broke
/// <c>Recipe.recipeInstructions</c> (which then stopped short of <c>HowToStep</c>). Keeping
/// <c>K</c> paths alive and ranking them by geometric-mean edge probability lets deeper
/// evidence repair an ambiguous early decision, and compares a shallow landing point with a
/// deep one fairly.
/// </para>
/// <para>
/// <c>__stop</c> is listed first and framed as the default because search engines key rich
/// results off the common types (Review, Offer, Person, Place): narrowing to an exotic
/// subtype is a real-world regression, not extra precision.
/// </para>
/// <para>
/// Every level batches all subjects' questions into as few requests as the chunk size
/// allows, so depth costs requests, not subjects.
/// </para>
/// </remarks>
internal sealed class BeamSearch
{
    private readonly JudgmentSession _session;
    private readonly ISchemaTypeGraph _graph;
    private readonly int _maxOptions;
    private readonly ILogger _logger;

    public BeamSearch(JudgmentSession session, ISchemaTypeGraph graph, int maxOptionsPerChoice, ILogger logger)
    {
        _session = session;
        _graph = graph;
        _maxOptions = Math.Clamp(maxOptionsPerChoice, 1, 254);
        _logger = logger;
    }

    /// <summary>
    /// Runs one descent per subject and returns, per subject key, its final beams best first.
    /// A subject whose root has no children comes back as one finished beam on the root.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Beam>>> DescendAsync(
        object state,
        IReadOnlyList<DescentSubject> subjects,
        int beamWidth,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        beamWidth = Math.Max(1, beamWidth);
        maxDepth = Math.Max(0, maxDepth);

        var states = subjects
            .Select(s => new SubjectState(s, [new Beam(s.Root, 0, 0, false, [s.Root])]))
            .ToList();

        for (var depth = 0; depth < maxDepth; depth++)
        {
            var questions = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
            var asked = new List<Asked>();

            foreach (var subject in states)
            {
                for (var i = 0; i < subject.Beams.Count; i++)
                {
                    var beam = subject.Beams[i];
                    if (beam.Done)
                        continue;

                    var children = _graph.ChildrenOf(beam.Type);
                    if (children.Count == 0)
                    {
                        subject.Beams[i] = beam with { Done = true };
                        continue;
                    }

                    var offered = children.Take(_maxOptions).ToList();
                    var id = JudgmentSession.Id("desc", subject.Subject.Key, depth.ToString(), i.ToString());
                    asked.Add(new Asked(subject, i, id, offered));
                    questions[id] = BuildLevelQuestion(subject.Subject.Description, beam.Type, offered);
                }
            }

            if (asked.Count == 0)
                break;

            var answers = await _session.AskChunkedAsync(state, questions, $"descend/{depth}", cancellationToken)
                .ConfigureAwait(false);

            foreach (var subject in states)
            {
                var next = new List<Beam>();
                for (var i = 0; i < subject.Beams.Count; i++)
                {
                    var beam = subject.Beams[i];
                    if (beam.Done)
                    {
                        next.Add(beam);
                        continue;
                    }

                    var entry = asked.FirstOrDefault(a => ReferenceEquals(a.Subject, subject) && a.BeamIndex == i);
                    var distribution = entry is null ? [] : JudgmentSession.Distribution(answers, entry.Id);
                    if (distribution.Count == 0)
                    {
                        // Missing or malformed answer: the beam stops where it is, unpenalised.
                        next.Add(beam with { Done = true });
                        continue;
                    }

                    // Expand every option worth following; the beam cut below does the pruning.
                    var expanded = 0;
                    foreach (var (option, p) in distribution)
                    {
                        if (string.Equals(option, JudgmentSession.Stop, StringComparison.Ordinal))
                        {
                            next.Add(beam with { LogP = beam.LogP + Math.Log(p), Steps = beam.Steps + 1, Done = true });
                            expanded++;
                        }
                        else if (entry!.Offered.Contains(option, StringComparer.Ordinal))
                        {
                            next.Add(new Beam(option, beam.LogP + Math.Log(p), beam.Steps + 1, false, [.. beam.Path, option]));
                            expanded++;
                        }
                    }

                    if (expanded == 0)
                    {
                        // Every option the answer named was one the question never offered. The
                        // wire is untrusted, so this degrades exactly like a missing answer: the
                        // beam stops where it is, unpenalised, rather than vanishing and leaving
                        // the subject with no beam at all.
                        next.Add(beam with { Done = true });
                    }
                }

                subject.Beams = next
                    .OrderByDescending(b => b.GeoMean)
                    .Take(beamWidth)
                    .ToList();
            }

            if (states.All(s => s.Beams.All(b => b.Done)))
                break;
        }

        var result = new Dictionary<string, IReadOnlyList<Beam>>(StringComparer.Ordinal);
        foreach (var subject in states)
        {
            var ranked = subject.Beams.OrderByDescending(b => b.GeoMean).ToList();
            result[subject.Subject.Key] = ranked;
            _logger.LogDebug("TypeSafe descent {Key}: {Type} (geo-mean {GeoMean:F2}, {Alternatives} alternative(s))",
                subject.Subject.Key, ranked[0].Type, ranked[0].GeoMean, ranked.Count - 1);
        }

        return result;
    }

    private static SystemOneQuestion BuildLevelQuestion(string subjectDescription, string current, IReadOnlyList<string> children)
    {
        var criteria = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [JudgmentSession.Stop] = $"Stay with {current} — nothing in the content positively demands a narrower type.",
        };
        foreach (var child in children)
            criteria[child] = $"The content shows it is specifically a {child}, not just any {current}.";

        return SystemOneQuestion.Choice(
            $"{subjectDescription} It is a {current}, and {current} is an acceptable, widely-understood answer. "
            + "Only choose a more specific subtype if the content clearly shows it is that narrower kind of thing.",
            criteria);
    }

    private sealed class SubjectState(DescentSubject subject, List<Beam> beams)
    {
        public DescentSubject Subject { get; } = subject;

        public List<Beam> Beams { get; set; } = beams;
    }

    private sealed record Asked(SubjectState Subject, int BeamIndex, string Id, IReadOnlyList<string> Offered);
}

/// <summary>One thing to classify: a key for its question ids, the type to start from, and the prose that describes it.</summary>
internal sealed record DescentSubject(string Key, string Root, string Description);

/// <summary>
/// One path through the tree. <see cref="GeoMean"/> is the geometric mean of the edge
/// probabilities along it (1 for a path that never moved), the ranking the eval settled on
/// because it compares paths of different lengths fairly.
/// </summary>
internal sealed record Beam(string Type, double LogP, int Steps, bool Done, IReadOnlyList<string> Path)
{
    public double GeoMean => Steps == 0 ? 1 : Math.Exp(LogP / Steps);
}
