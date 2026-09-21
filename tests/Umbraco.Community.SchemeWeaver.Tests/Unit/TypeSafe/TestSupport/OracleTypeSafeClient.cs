using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;

/// <summary>
/// One mapping the oracle "knows" is right: the schema property, the content properties that
/// feed it (the first is the primary claimant), its shape, and its inner bindings. This is the
/// C# twin of one gold mapping row in <c>eval/gold.mjs</c>.
/// </summary>
/// <remarks>
/// v2 adds two kinds of row. A cross-node row (<see cref="CrossNode"/>) names a neighbour type
/// and one of its properties; it never takes part in the local bind, because the same alias
/// (a listing's <c>title</c>) can legitimately exist on the page itself. A routes row
/// (<see cref="BlockRoutes"/>) carries one <see cref="ExpectedRoute"/> per element type,
/// recursively for blocks inside blocks; its v1 fields (<see cref="NestedType"/>,
/// <see cref="Inner"/>) are derived from the first kept route so the same fixture also answers
/// the v1 questions under <c>RoutesMode.Off</c>.
/// </remarks>
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

    /// <summary>v2: the per-element-type routes of a nested Block List row (null for a v1-shaped row).</summary>
    public IReadOnlyList<ExpectedRoute>? Routes { get; init; }

    /// <summary>v2: the neighbour type a cross-node row reads from (null for a local row).</summary>
    public string? CrossNodeTypeAlias { get; init; }

    /// <summary>v2: the neighbour property a cross-node row reads.</summary>
    public string? CrossNodeProperty { get; init; }

    public bool IsCrossNode => CrossNodeTypeAlias is not null;

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

    /// <summary>
    /// A <c>parent</c>/<c>ancestor</c>/<c>sibling</c> row: <paramref name="relation"/> is the core
    /// source-type value, <paramref name="typeAlias"/> the neighbour type and
    /// <paramref name="propertyAlias"/> its property. Answered only to <c>cross__</c> questions.
    /// </summary>
    public static ExpectedMapping CrossNode(string schemaProperty, string relation, string typeAlias, string propertyAlias, double confidence = 0.9)
        => new(schemaProperty, [], relation)
        {
            CrossNodeTypeAlias = typeAlias,
            CrossNodeProperty = propertyAlias,
            Confidence = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [propertyAlias] = confidence },
        };

    /// <summary>A nested Block List row planned per element type; see <see cref="ExpectedRoute"/>.</summary>
    public static ExpectedMapping BlockRoutes(string schemaProperty, string blockListAlias, params ExpectedRoute[] routes)
    {
        var first = routes.FirstOrDefault(r => r.NestedType is not null);
        return new ExpectedMapping(schemaProperty, [blockListAlias], "blockContent", first?.NestedType, Inner: first?.Inner)
        {
            Routes = routes,
        };
    }
}

/// <summary>
/// What the oracle knows about one element type of a nested Block List: the nested type it
/// becomes (<c>null</c> means "does not belong here", answered as <c>__none</c> to the planner's
/// root question), which nested property each field populates, and for a field that is itself a
/// Block List either the routes of its inner element types or the field that flattens it to a
/// string list.
/// </summary>
internal sealed record ExpectedRoute(
    string BlockAlias,
    string? NestedType,
    IReadOnlyList<(string ContentAlias, string SchemaProperty)> Inner)
{
    /// <summary>Block-editor field alias to the routes of the element types inside it.</summary>
    public Dictionary<string, ExpectedRoute[]> NestedRoutes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Block-editor field alias to the inner field that carries the text when it flattens to strings.</summary>
    public Dictionary<string, string> StringListFields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ExpectedRoute Of(string blockAlias, string nestedType, params (string ContentAlias, string SchemaProperty)[] inner)
        => new(blockAlias, nestedType, inner);

    /// <summary>An element type the oracle judges not to belong under the property at all.</summary>
    public static ExpectedRoute Skip(string blockAlias)
        => new(blockAlias, null, []);

    public ExpectedRoute WithNested(string fieldAlias, params ExpectedRoute[] routes)
    {
        NestedRoutes[fieldAlias] = routes;
        return this;
    }

    public ExpectedRoute WithStringList(string fieldAlias, string textField)
    {
        StringListFields[fieldAlias] = textField;
        return this;
    }
}

/// <summary>
/// The gold oracle from <c>eval/oracle.mjs</c>, in C#: a hypothetical perfect model that
/// answers every question from a known expected mapping, keyed by the question-id prefixes
/// the mapper uses (<c>bind__</c>, <c>shape__</c>, <c>entity__</c>, <c>root__</c>,
/// <c>desc__</c>, <c>inner__</c>, <c>strlist__</c>, and from v2 <c>cross__</c> plus the route
/// planner's path-qualified forms of <c>root</c>, <c>desc</c>, <c>inner</c>, <c>shape</c> and
/// <c>strlist</c>). With it the pipeline's MECHANICS — question assembly, claim collisions,
/// descent, resolver-config assembly, the priors merge and gating — are testable without the
/// real API, and any drift in what the mapper asks shows up as a wrong answer rather than a
/// silent pass.
/// </summary>
/// <remarks>
/// Planner ids are path-qualified: <c>root__{prop}__{block}</c>,
/// <c>desc__{prop}__{block}[__{field}__{innerBlock}]__{depth}__{i}</c>,
/// <c>inner__{prop}__{block}[__{field}__{innerBlock}]__{fieldAlias}</c>,
/// <c>shape__{prop}__{block}[…]__{field}</c> and <c>strlist__{prop}__{block}[…]__{field}</c>.
/// The oracle walks the path through <see cref="ExpectedMapping.Routes"/>; an id whose path
/// does not start with a known block alias is a v1 id and is answered as v1 (that is what
/// <c>RoutesMode.Off</c> emits for the same fixture).
/// </remarks>
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
        => _expected.FirstOrDefault(m => !m.IsCrossNode && string.Equals(m.SchemaProperty, schemaProperty, StringComparison.OrdinalIgnoreCase));

    private ExpectedMapping? CrossNodeBySchema(string schemaProperty)
        => _expected.FirstOrDefault(m => m.IsCrossNode && string.Equals(m.SchemaProperty, schemaProperty, StringComparison.OrdinalIgnoreCase));

    private SystemOneAnswer Answer(string id, SystemOneQuestion question)
    {
        // Ids are "<kind>__<part>__<part>"; a built-in alias like "__name" splits into empty
        // parts, so the tail is re-joined exactly as oracle.mjs does with parts.slice(1).join('__').
        var parts = id.Split("__");
        var kind = parts[0];
        var tail = string.Join("__", parts.Skip(1));
        var m = parts.Length > 1 ? BySchema(parts[1]) : null;

        switch (kind)
        {
            case "bind":
            {
                var hit = _expected.FirstOrDefault(x => !x.IsCrossNode && x.ContentProperties.Contains(tail, StringComparer.OrdinalIgnoreCase));
                var target = hit is null ? null : FakeTypeSafeClient.OptionNamed(question, hit.SchemaProperty);
                if (target is null)
                    return FakeTypeSafeClient.Choice(JudgmentSession.None, 0.9);

                var confidence = hit!.Confidence.TryGetValue(tail, out var c) ? c : 0.95;
                return FakeTypeSafeClient.Choice(target, confidence);
            }

            case "cross":
            {
                // Option keys are code-generated as {relation}__{typeAlias}__{propertyAlias}.
                var cross = parts.Length > 1 ? CrossNodeBySchema(parts[1]) : null;
                var key = cross is null
                    ? null
                    : FakeTypeSafeClient.OptionNamed(question, JudgmentSession.Id(cross.SourceType, cross.CrossNodeTypeAlias!, cross.CrossNodeProperty!));
                if (key is null)
                    return FakeTypeSafeClient.Choice(JudgmentSession.None, 0.9);

                var confidence = cross!.Confidence.TryGetValue(cross.CrossNodeProperty!, out var c) ? c : 0.95;
                return FakeTypeSafeClient.Choice(key, confidence);
            }

            case "shape":
            {
                if (parts.Length == 2)
                    return FakeTypeSafeClient.Choice(m?.ExtractAs == "stringList" ? "stringList" : "nested");

                // Planner: the nested block-list field's shape.
                var route = WalkRoute(m, parts, skipTail: 1);
                var isStringList = route is not null && route.StringListFields.ContainsKey(parts[^1]);
                return FakeTypeSafeClient.Choice(isStringList ? "stringList" : "nested");
            }

            case "entity":
                return FakeTypeSafeClient.Noul(m?.SourceType == "complexType" ? 0.97 : 0.03);

            // A v1-shaped expectation (BlockNested) still meets planner ids under Auto, where every
            // nested block row is planned per element type: root/desc take the expected nested type
            // for every element, and inner falls back to the field alias (the last id part).
            case "root":
            {
                if (parts.Length == 2 || m?.Routes is null)
                    return Descend(question, m?.NestedType);

                // Planner: per element type, __none means "this block type does not belong here".
                var route = WalkRoute(m, parts, skipTail: 0);
                if (route is null || route.NestedType is null)
                    return FakeTypeSafeClient.Choice(JudgmentSession.None, 0.9);

                return Descend(question, route.NestedType);
            }

            case "desc":
            {
                // v1: desc__{prop}__{depth}__{i}; planner: desc__{prop}__{path…}__{depth}__{i}.
                var want = (parts.Length == 4 && int.TryParse(parts[2], out _)) || m?.Routes is null
                    ? m?.NestedType
                    : WalkRoute(m, parts, skipTail: 2)?.NestedType;
                return Descend(question, want);
            }

            case "strlist":
            {
                var want = parts.Length == 2
                    ? m?.StringListField
                    : WalkRoute(m, parts, skipTail: 1) is { } route && route.StringListFields.TryGetValue(parts[^1], out var field) ? field : null;
                return FakeTypeSafeClient.Choice(
                    FakeTypeSafeClient.OptionNamed(question, want) ?? FakeTypeSafeClient.CriteriaOf(question).Keys.First());
            }

            case "inner":
            {
                // Planner ids carry the block path; a v1 id (or a built-in alias such as
                // inner__Author____name, whose empty part never matches a block alias) falls
                // through to the v1 reading.
                var route = parts.Length > 3 ? WalkRoute(m, parts, skipTail: 1) : null;
                (string ContentAlias, string SchemaProperty)? hit;
                if (route is not null)
                {
                    hit = route.Inner.Cast<(string ContentAlias, string SchemaProperty)?>()
                        .FirstOrDefault(i => string.Equals(i!.Value.ContentAlias, parts[^1], StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    // v1 first (the whole tail, so a built-in such as __name survives its empty
                    // part), then the last part alone for a planner id over a v1 expectation.
                    var innerAlias = string.Join("__", parts.Skip(2));
                    hit = m?.Inner?.Cast<(string ContentAlias, string SchemaProperty)?>()
                        .FirstOrDefault(i => string.Equals(i!.Value.ContentAlias, innerAlias, StringComparison.OrdinalIgnoreCase))
                        ?? m?.Inner?.Cast<(string ContentAlias, string SchemaProperty)?>()
                        .FirstOrDefault(i => string.Equals(i!.Value.ContentAlias, parts[^1], StringComparison.OrdinalIgnoreCase));
                }

                var target = hit is { } h ? FakeTypeSafeClient.OptionNamed(question, h.SchemaProperty) : null;
                return FakeTypeSafeClient.Choice(target ?? JudgmentSession.None);
            }

            default:
                return FakeTypeSafeClient.Choice(JudgmentSession.None, 0.5);
        }
    }

    /// <summary>Descent: the option that IS the expected nested type, else an ancestor of it, else stop (or the first option).</summary>
    private SystemOneAnswer Descend(SystemOneQuestion question, string? want)
    {
        var options = FakeTypeSafeClient.CriteriaOf(question).Keys.ToList();
        var stopOrFirst = options.Contains(JudgmentSession.Stop) ? JudgmentSession.Stop : options[0];
        if (want is null)
            return FakeTypeSafeClient.Choice(stopOrFirst, 0.9);

        var exact = FakeTypeSafeClient.OptionNamed(question, want);
        if (exact is not null)
            return FakeTypeSafeClient.Choice(exact);

        var onPath = options.FirstOrDefault(o => o != JudgmentSession.Stop && o != JudgmentSession.None && _graph.IsSubtypeOf(want, o));
        return FakeTypeSafeClient.Choice(onPath ?? stopOrFirst, 0.9);
    }

    /// <summary>
    /// Follows a planner path <c>[block, (field, innerBlock)*]</c> (the id parts after the schema
    /// property, minus <paramref name="skipTail"/> trailing parts) through the expected routes.
    /// Null when the mapping has no routes or any segment is unknown.
    /// </summary>
    private static ExpectedRoute? WalkRoute(ExpectedMapping? m, string[] parts, int skipTail)
    {
        if (m?.Routes is null || parts.Length - 2 - skipTail < 1)
            return null;

        var path = parts.Skip(2).Take(parts.Length - 2 - skipTail).ToList();
        var route = FindRoute(m.Routes, path[0]);
        for (var i = 1; route is not null && i + 1 < path.Count; i += 2)
        {
            route = route.NestedRoutes.TryGetValue(path[i], out var inner) ? FindRoute(inner, path[i + 1]) : null;
        }

        return route;
    }

    /// <summary>A route by block alias, tolerating the <c>_2</c> suffix the planner appends to a colliding path.</summary>
    private static ExpectedRoute? FindRoute(IReadOnlyList<ExpectedRoute> routes, string blockPart)
        => routes.FirstOrDefault(r => string.Equals(r.BlockAlias, blockPart, StringComparison.OrdinalIgnoreCase))
           ?? routes.FirstOrDefault(r => blockPart.StartsWith(r.BlockAlias + "_", StringComparison.OrdinalIgnoreCase));
}
