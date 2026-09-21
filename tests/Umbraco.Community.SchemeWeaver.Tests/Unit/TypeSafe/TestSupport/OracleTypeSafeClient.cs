using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;

/// <summary>
/// One mapping the oracle "knows" is right: the schema property, the content properties that
/// feed it (the first is the primary claimant), its shape, and its inner bindings. This is the
/// C# twin of one gold mapping row in <c>eval/gold.mjs</c>.
/// </summary>
internal sealed record ExpectedMapping(
    string SchemaProperty,
    IReadOnlyList<string> ContentProperties,
    string SourceType = "property",
    string? NestedType = null,
    string? ExtractAs = null,
    string? StringListField = null,
    IReadOnlyList<(string ContentAlias, string SchemaProperty)>? Inner = null)
{
    /// <summary>Per-alias bind confidence overrides (0–1); anything not listed answers at 0.95.</summary>
    public Dictionary<string, double> Confidence { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static ExpectedMapping Property(string schemaProperty, string contentProperty, double? confidence = null)
    {
        var m = new ExpectedMapping(schemaProperty, [contentProperty]);
        if (confidence is { } c)
            m.Confidence[contentProperty] = c;
        return m;
    }

    public static ExpectedMapping Complex(string schemaProperty, string nestedType, params (string ContentAlias, string SchemaProperty)[] inner)
        => new(schemaProperty, inner.Select(i => i.ContentAlias).ToList(), "complexType", nestedType, Inner: inner);

    public static ExpectedMapping BlockNested(string schemaProperty, string blockAlias, string nestedType, params (string ContentAlias, string SchemaProperty)[] inner)
        => new(schemaProperty, [blockAlias], "blockContent", nestedType, Inner: inner);

    public static ExpectedMapping BlockStringList(string schemaProperty, string blockAlias, string field)
        => new(schemaProperty, [blockAlias], "blockContent", ExtractAs: "stringList", StringListField: field);
}

/// <summary>
/// The gold oracle from <c>eval/oracle.mjs</c>, in C#: a hypothetical perfect model that
/// answers every question from a known expected mapping, keyed by the question-id prefixes
/// the mapper uses (<c>bind__</c>, <c>shape__</c>, <c>entity__</c>, <c>root__</c>,
/// <c>desc__</c>, <c>inner__</c>, <c>strlist__</c>). With it the pipeline's MECHANICS —
/// question assembly, claim collisions, descent, resolver-config assembly, the priors merge
/// and gating — are testable without the real API, and any drift in what the mapper asks
/// shows up as a wrong answer rather than a silent pass.
/// </summary>
internal sealed class OracleTypeSafeClient : ITypeSafeClient
{
    private readonly IReadOnlyList<ExpectedMapping> _expected;
    private readonly ISchemaTypeGraph _graph;

    public OracleTypeSafeClient(ISchemaTypeGraph graph, params ExpectedMapping[] expected)
    {
        _graph = graph;
        _expected = expected;
        Inner = new FakeTypeSafeClient(Answer);
    }

    /// <summary>The recording client underneath, for request/state assertions.</summary>
    public FakeTypeSafeClient Inner { get; }

    public bool IsConfigured => Inner.IsConfigured;

    public Task<SystemOneResponse> AskAsync(
        object state,
        IReadOnlyDictionary<string, SystemOneQuestion> questions,
        CancellationToken cancellationToken = default)
        => Inner.AskAsync(state, questions, cancellationToken);

    private ExpectedMapping? BySchema(string schemaProperty)
        => _expected.FirstOrDefault(m => string.Equals(m.SchemaProperty, schemaProperty, StringComparison.OrdinalIgnoreCase));

    private SystemOneAnswer Answer(string id, SystemOneQuestion question)
    {
        // Ids are "<kind>__<part>__<part>"; a built-in alias like "__name" splits into empty
        // parts, so the tail is re-joined exactly as oracle.mjs does with parts.slice(1).join('__').
        var parts = id.Split("__");
        var kind = parts[0];
        var tail = string.Join("__", parts.Skip(1));

        switch (kind)
        {
            case "bind":
            {
                var hit = _expected.FirstOrDefault(m => m.ContentProperties.Contains(tail, StringComparer.OrdinalIgnoreCase));
                var target = hit is null ? null : FakeTypeSafeClient.OptionNamed(question, hit.SchemaProperty);
                if (target is null)
                    return FakeTypeSafeClient.Choice(JudgmentSession.None, 0.9);

                var confidence = hit!.Confidence.TryGetValue(tail, out var c) ? c : 0.95;
                return FakeTypeSafeClient.Choice(target, confidence);
            }

            case "shape":
            {
                var m = BySchema(parts[1]);
                return FakeTypeSafeClient.Choice(m?.ExtractAs == "stringList" ? "stringList" : "nested");
            }

            case "entity":
                return FakeTypeSafeClient.Noul(BySchema(parts[1])?.SourceType == "complexType" ? 0.97 : 0.03);

            case "root":
            case "desc":
            {
                // Descent: the option that IS the expected nested type, else an ancestor of it.
                var want = BySchema(parts[1])?.NestedType;
                var options = FakeTypeSafeClient.CriteriaOf(question).Keys.ToList();
                var stopOrFirst = options.Contains(JudgmentSession.Stop) ? JudgmentSession.Stop : options[0];
                if (want is null)
                    return FakeTypeSafeClient.Choice(stopOrFirst, 0.9);

                var exact = FakeTypeSafeClient.OptionNamed(question, want);
                if (exact is not null)
                    return FakeTypeSafeClient.Choice(exact);

                var onPath = options.FirstOrDefault(o => o != JudgmentSession.Stop && _graph.IsSubtypeOf(want, o));
                return FakeTypeSafeClient.Choice(onPath ?? stopOrFirst, 0.9);
            }

            case "strlist":
            {
                var want = BySchema(parts[1])?.StringListField;
                return FakeTypeSafeClient.Choice(
                    FakeTypeSafeClient.OptionNamed(question, want) ?? FakeTypeSafeClient.CriteriaOf(question).Keys.First());
            }

            case "inner":
            {
                var m = BySchema(parts[1]);
                var innerAlias = string.Join("__", parts.Skip(2));
                var hit = m?.Inner?.FirstOrDefault(i => string.Equals(i.ContentAlias, innerAlias, StringComparison.OrdinalIgnoreCase));
                var target = hit is { } h ? FakeTypeSafeClient.OptionNamed(question, h.SchemaProperty) : null;
                return FakeTypeSafeClient.Choice(target ?? JudgmentSession.None);
            }

            default:
                return FakeTypeSafeClient.Choice(JudgmentSession.None, 0.5);
        }
    }
}
