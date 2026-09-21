using Microsoft.Extensions.Logging;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

/// <summary>
/// Plans a nested Block List row PER ELEMENT TYPE, recursing into blocks inside blocks, and
/// decides whether the result needs the core's per-element <c>routes</c> config or fits the
/// v1 single-type <c>nestedMappings</c> shape. Code owns every rule; the model judges only
/// which type family a block belongs to, which nested type it is (beam search), which
/// property each field populates, and whether a nested list is a list of things or of strings.
/// </summary>
/// <remarks>
/// <para>
/// v1 planned one nested type for the whole list from the fields of every element type merged
/// together, so a list mixing "FAQ" and "Team member" blocks could only be all <c>Question</c>
/// or all <c>Person</c>; and a field that was itself a Block List was bound like a scalar and
/// given <c>wrapInType "Thing"</c>, a dead inner mapping the core's resolver warns on and
/// drops. Both are v1's documented limits. Planning per element type with a depth-batched
/// walk removes them without changing what a single-element list produces.
/// </para>
/// <para>
/// Every level batches the same question kind across all subjects at that depth (root/skip,
/// then descent, then field binding, then nested shape), so depth costs requests, not
/// subjects, exactly as <see cref="BeamSearch"/> does. Descent always starts from
/// <c>RangeOf(parentType, targetProperty)</c>, so a route's nested type can never be out of
/// range for the property it lands on, at any depth.
/// </para>
/// </remarks>
internal sealed class BlockRoutePlanner
{
    private const string Nested = "nested";
    private const string StringList = "stringList";

    /// <summary>The API allows 255 options per Choice; one slot is reserved for <c>__none</c>.</summary>
    private const int HardOptionCap = 254;

    /// <summary>
    /// Collection-valued targets offered FIRST when a field is itself a block list. Option order
    /// was measured to matter (the ranked bind list bound block properties noticeably more
    /// confidently than registry order), and a repeating list of blocks is, by construction, a
    /// collection of things rather than a scalar of the nested type.
    /// </summary>
    internal static readonly string[] CollectionTargets = ["MainEntity", "HasPart", "About", "ItemListElement", "MainContentOfPage"];

    private readonly JudgmentSession _session;
    private readonly ISchemaTypeGraph _graph;
    private readonly Func<string, IReadOnlyList<SchemaPropertyInfo>> _nestedTargets;
    private readonly TypeSafeOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// <paramref name="nestedTargets"/> supplies the inner-binding targets of a nested type as
    /// the mapper offers them (registry order, capped), so the planner and the v1 inner round
    /// can never disagree about what a nested type's fields may bind to.
    /// </summary>
    public BlockRoutePlanner(
        JudgmentSession session,
        ISchemaTypeGraph graph,
        Func<string, IReadOnlyList<SchemaPropertyInfo>> nestedTargets,
        TypeSafeOptions options,
        ILogger logger)
    {
        _session = session;
        _graph = graph;
        _nestedTargets = nestedTargets;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Plans every subject, one plan per subject key, walking blocks-inside-blocks up to
    /// <see cref="TypeSafeOptions.MaxBlockRouteDepth"/> levels below the top-level element types.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, BlockRoutePlan>> PlanAsync(
        object state,
        IReadOnlyList<BlockListSubject> subjects,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var perSubject = new List<(BlockListSubject Subject, List<ElementNode> Nodes)>();
        var level = new List<ElementNode>();

        foreach (var subject in subjects)
        {
            var nodes = new List<ElementNode>();
            foreach (var element in subject.Elements)
            {
                var node = new ElementNode(
                    UniquePath(paths, subject.Key, element.Alias),
                    element,
                    subject.ParentSchemaType,
                    subject.TargetProperty,
                    depth: 0,
                    $"in the Umbraco Block List `{subject.ListAlias}`");
                nodes.Add(node);
                level.Add(node);
            }

            perSubject.Add((subject, nodes));
        }

        for (var depth = 0; level.Count > 0; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ChooseRootsAsync(state, level, depth, cancellationToken).ConfigureAwait(false);
            await DescendAsync(state, level, cancellationToken).ConfigureAwait(false);
            await BindFieldsAsync(state, level, depth, cancellationToken).ConfigureAwait(false);
            var planned = level.Count;
            level = await PlanNestedFieldsAsync(state, level, depth, paths, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("TypeSafe routes depth {Depth}: {Elements} element type(s) planned, {Next} nested element type(s) queued",
                depth, planned, level.Count);
        }

        var plans = new Dictionary<string, BlockRoutePlan>(StringComparer.Ordinal);
        foreach (var (subject, nodes) in perSubject)
            plans[subject.Key] = Build(subject, nodes);

        return plans;
    }

    // ---------------------------------------------------------------------------
    // (a) root family, or skip
    // ---------------------------------------------------------------------------

    /// <summary>
    /// One Choice per element type over the declared range of the property it lands on, with
    /// <c>__none</c> FIRST and framed as the default: a block type that does not belong under
    /// the property (a "call to action" block in a body-sections list emitted as
    /// <c>hasPart</c>) is left out rather than forced into the nearest type.
    /// </summary>
    private async Task ChooseRootsAsync(object state, List<ElementNode> level, int depth, CancellationToken cancellationToken)
    {
        var questions = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
        var asked = new List<(ElementNode Node, string Id, IReadOnlyList<string> Roots)>();

        foreach (var node in level)
        {
            var roots = _graph.RangeOf(node.ParentSchemaType, node.TargetProperty);
            if (roots.Count == 0)
            {
                // Defensive: subjects are only created for properties with a usable range.
                node.Skipped = true;
                continue;
            }

            var id = JudgmentSession.UniqueId(questions, "root", node.Path);
            var criteria = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [JudgmentSession.None] = $"This block type does not belong under {node.ParentSchemaType}.{node.TargetProperty}; leave it out.",
            };
            foreach (var root in roots.Take(HardOptionCap))
                criteria[root] = $"It is a {root} (or a more specific kind of {root}).";

            questions[id] = SystemOneQuestion.Choice(
                $"{node.Describe()} Which of these Schema.org types is the right family for it? "
                + $"Choose {JudgmentSession.None} if blocks of this type should not be emitted under "
                + $"{node.ParentSchemaType}.{node.TargetProperty} at all.",
                criteria);
            asked.Add((node, id, roots));
        }

        var answers = await _session.AskChunkedAsync(state, questions, $"routes/{depth}/root", cancellationToken).ConfigureAwait(false);

        foreach (var (node, id, roots) in asked)
        {
            var choice = JudgmentSession.Choice(answers, id, roots);
            if (choice is not null)
            {
                node.Root = choice;
            }
            else if (AnsweredNone(answers, id))
            {
                node.Skipped = true;
            }
            else
            {
                // Missing or malformed answer: keep the block on the first declared root, as v1
                // did, rather than silently dropping content the bind round chose to emit.
                node.Root = roots[0];
            }
        }
    }

    // ---------------------------------------------------------------------------
    // (b) nested type by beam search
    // ---------------------------------------------------------------------------

    private async Task DescendAsync(object state, List<ElementNode> level, CancellationToken cancellationToken)
    {
        var active = level.Where(n => !n.Skipped && n.Root is not null).ToList();
        if (active.Count == 0)
            return;

        var subjects = active.Select(n => new DescentSubject(n.Path, n.Root!, n.Describe())).ToList();
        var beamSearch = new BeamSearch(_session, _graph, _options.MaxOptionsPerChoice, _logger);
        var beams = await beamSearch
            .DescendAsync(state, subjects, _options.BeamWidth, _options.MaxDescentDepth, cancellationToken)
            .ConfigureAwait(false);

        foreach (var node in active)
        {
            var winner = beams.TryGetValue(node.Path, out var ranked) && ranked.Count > 0 ? ranked[0] : null;
            node.NestedType = winner?.Type ?? node.Root;
        }
    }

    // ---------------------------------------------------------------------------
    // (c) field bindings
    // ---------------------------------------------------------------------------

    /// <summary>
    /// One Choice per field of each kept element type over the nested type's properties. A
    /// field that is itself a block list is asked only when it can be planned (it has element
    /// types in the snapshot and depth remains); otherwise it is dropped from the route
    /// outright, which is the fix for v1's dead <c>wrapInType "Thing"</c> inner mapping.
    /// </summary>
    private async Task BindFieldsAsync(object state, List<ElementNode> level, int depth, CancellationToken cancellationToken)
    {
        var questions = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
        var asked = new List<(ElementNode Node, FieldNode Field, string Id, IReadOnlyList<string> Allowed)>();

        foreach (var node in level)
        {
            if (node.Skipped || node.NestedType is null)
                continue;

            var targets = _nestedTargets(node.NestedType);
            var allowed = targets.Select(t => t.Name).ToList();
            var scalarCriteria = BuildCriteria(targets, $"None of these — this field has no good {node.NestedType} property.");
            Dictionary<string, string>? blockCriteria = null;

            foreach (var field in node.Element.PropertyInfos)
            {
                var fieldNode = new FieldNode(field);
                node.Fields.Add(fieldNode);

                string guidance;
                Dictionary<string, string> criteria;
                if (fieldNode.IsBlockEditor)
                {
                    if (!CanPlanNested(field, depth))
                        continue;

                    blockCriteria ??= BuildCriteria(CollectionFirst(targets), $"None of these — this nested list has no good {node.NestedType} property.");
                    criteria = blockCriteria;
                    guidance = " This field is itself a repeating list of blocks ("
                        + string.Join("; ", field.NestedBlockElementTypes.Select(DescribeElement))
                        + "); it belongs on a property that holds a collection of things, not on a scalar.";
                }
                else
                {
                    criteria = scalarCriteria;
                    guidance = string.Empty;
                }

                // Unique within the round: two field aliases can sanitise to the same id, and a
                // duplicate key would overwrite the first field's question.
                var id = JudgmentSession.UniqueId(questions, "inner", node.Path, field.Alias);
                questions[id] = SystemOneQuestion.Choice(
                    $"Inside the block `{node.Element.Alias}` ({node.Context}), the field `{field.Alias}` "
                    + $"(label \"{DisplayName(field)}\", editor {field.EditorAlias}) holds content.{guidance} "
                    + $"Each block is emitted as a Schema.org {node.NestedType}. "
                    + $"Which property of {node.NestedType} should this field populate?",
                    criteria);
                asked.Add((node, fieldNode, id, allowed));
            }
        }

        var answers = await _session.AskChunkedAsync(state, questions, $"routes/{depth}/inner", cancellationToken).ConfigureAwait(false);

        foreach (var (node, field, id, allowed) in asked)
        {
            var choice = JudgmentSession.Choice(answers, id, allowed);
            if (choice is null)
                continue;

            field.SchemaProperty = choice;

            // Scalar into an entity-ranged property: wrap in the first registry type of the
            // range, as v1 did. wrapInProperty is deliberately absent so the core infers the
            // receiving property from the field name at render time.
            if (!field.IsBlockEditor)
                field.WrapInType = _graph.RangeOf(node.NestedType!, choice).FirstOrDefault();
        }
    }

    // ---------------------------------------------------------------------------
    // (d) nested block-list fields: string list by rule, or nested subjects one level down
    // ---------------------------------------------------------------------------

    private async Task<List<ElementNode>> PlanNestedFieldsAsync(
        object state,
        List<ElementNode> level,
        int depth,
        HashSet<string> paths,
        CancellationToken cancellationToken)
    {
        var next = new List<ElementNode>();
        var questions = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
        var asked = new List<(ElementNode Node, FieldNode Field, string ShapeId, string TextId, IReadOnlyList<string> InnerAliases)>();

        foreach (var node in level)
        {
            if (node.Skipped || node.NestedType is null)
                continue;

            foreach (var field in node.Fields)
            {
                if (!field.IsBlockEditor || field.SchemaProperty is null || !CanPlanNested(field.Field, depth))
                    continue;

                var inner = field.Field.NestedBlockElementTypes;
                var innerFields = UnionFields(inner);
                if (innerFields.Count == 0)
                    continue; // nothing to read: dropped

                // RULE, not judgment: when every inner element type carries at most one field,
                // the nested list can only be a list of strings (ingredients, tags, tools).
                if (inner.All(e => e.PropertyInfos.Count <= 1))
                {
                    field.ExtractAs = StringList;
                    field.NestedContentProperty = innerFields[0].Alias;
                    continue;
                }

                var shapeId = JudgmentSession.UniqueId(questions, "shape", node.Path, field.Alias);
                var textId = JudgmentSession.UniqueId(questions, "strlist", node.Path, field.Alias);
                var innerAliases = innerFields.Select(f => f.Alias).ToList();

                questions[shapeId] = SystemOneQuestion.Choice(
                    $"Inside the block `{node.Element.Alias}` (emitted as a {node.NestedType}), the Block List field "
                    + $"`{field.Alias}` supplies {node.NestedType}.{field.SchemaProperty}. "
                    + $"Each nested block holds these fields: {string.Join(", ", innerAliases.Select(a => $"`{a}`"))}. "
                    + "Should each nested block become one nested Schema.org object carrying several of those fields, "
                    + "or should the nested blocks flatten to a plain list of text values?",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [Nested] = "Each block is a distinct thing with several meaningful fields (a step, a question and its answer, a team member).",
                        [StringList] = "Each block carries essentially one meaningful label, so the list is just a list of strings (ingredients, tools, tags).",
                    });

                // Speculative, in the same request: if the answer is a string list, which field
                // carries the text. Asking both at once saves a sequential round per level.
                var textCriteria = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var f in innerFields.Take(HardOptionCap + 1))
                    textCriteria[f.Alias] = $"The `{f.Alias}` field (label \"{DisplayName(f)}\", {f.EditorAlias}).";

                questions[textId] = SystemOneQuestion.Choice(
                    $"If the nested blocks in `{field.Alias}` (inside the block `{node.Element.Alias}`) flatten to a plain list "
                    + $"of text values for {node.NestedType}.{field.SchemaProperty}, which single field of the nested block "
                    + "carries the text that should appear in that list?",
                    textCriteria);

                asked.Add((node, field, shapeId, textId, innerAliases));
            }
        }

        var answers = await _session.AskChunkedAsync(state, questions, $"routes/{depth}/nested", cancellationToken).ConfigureAwait(false);

        foreach (var (node, field, shapeId, textId, innerAliases) in asked)
        {
            var shape = JudgmentSession.Choice(answers, shapeId, [Nested, StringList]);
            var text = JudgmentSession.Choice(answers, textId, innerAliases) ?? innerAliases[0];
            var roots = string.Equals(shape, StringList, StringComparison.Ordinal)
                ? []
                : _graph.RangeOf(node.NestedType!, field.SchemaProperty!);

            if (roots.Count == 0)
            {
                // Judged a string list, or the chosen property has no entity range to descend
                // from: a nested list of things is not expressible there, so it flattens.
                field.ExtractAs = StringList;
                field.NestedContentProperty = text;
                continue;
            }

            var children = new List<ElementNode>();
            foreach (var innerElement in field.Field.NestedBlockElementTypes)
            {
                var child = new ElementNode(
                    UniquePath(paths, node.Path, field.Alias, innerElement.Alias),
                    innerElement,
                    node.NestedType!,
                    field.SchemaProperty!,
                    depth + 1,
                    $"inside the `{field.Alias}` field of a \"{DisplayName(node.Element)}\" block, itself a {node.NestedType} {node.Context}");
                children.Add(child);
                next.Add(child);
            }

            field.NestedElements = children;
        }

        return next;
    }

    // ---------------------------------------------------------------------------
    // Assembly and the routes-or-v1 decision
    // ---------------------------------------------------------------------------

    private static BlockRoutePlan Build(BlockListSubject subject, List<ElementNode> nodes)
    {
        var kept = nodes.Where(n => !n.Skipped && n.NestedType is not null).ToList();
        var skipped = nodes.Count - kept.Count;

        var routes = new List<PlannedRoute>();
        var hasNestedField = false;
        foreach (var node in kept)
        {
            var route = ToRoute(node);
            if (route is null)
                continue;

            routes.Add(route);
            hasNestedField |= route.PropertyMappings.Any(m => m.Routes is not null || m.ExtractAs is not null);
        }

        // v1 shape: fields merged across element types, first-declaring element wins, block
        // editor fields never included (they are exactly the dead inner mapping v1 emitted).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nestedMappings = new List<PlannedFieldMapping>();
        foreach (var node in kept)
        {
            foreach (var field in node.Fields)
            {
                if (field.IsBlockEditor || field.SchemaProperty is null || !seen.Add(field.Alias))
                    continue;

                nestedMappings.Add(new PlannedFieldMapping(field.SchemaProperty, field.Alias, field.WrapInType));
            }
        }

        var distinctTypes = kept.Select(k => k.NestedType!).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var requiresRoutes = distinctTypes > 1
            || hasNestedField
            || (skipped > 0 && kept.Count > 0)
            || SameFieldDifferentTargets(kept);

        return new BlockRoutePlan(
            subject.Key,
            kept.Count,
            skipped,
            requiresRoutes,
            routes,
            nestedMappings,
            kept.FirstOrDefault()?.NestedType);
    }

    private static PlannedRoute? ToRoute(ElementNode node)
    {
        var mappings = new List<PlannedFieldMapping>();
        foreach (var field in node.Fields)
        {
            var mapping = ToMapping(field);
            if (mapping is not null)
                mappings.Add(mapping);
        }

        return mappings.Count == 0 ? null : new PlannedRoute(node.Element.Alias, node.NestedType!, mappings);
    }

    private static PlannedFieldMapping? ToMapping(FieldNode field)
    {
        if (field.SchemaProperty is null)
            return null;

        if (!field.IsBlockEditor)
            return new PlannedFieldMapping(field.SchemaProperty, field.Alias, field.WrapInType);

        if (field.ExtractAs is not null && field.NestedContentProperty is not null)
            return new PlannedFieldMapping(field.SchemaProperty, field.Alias, ExtractAs: field.ExtractAs, NestedContentProperty: field.NestedContentProperty);

        if (field.NestedElements is { Count: > 0 } children)
        {
            var routes = children.Where(c => !c.Skipped && c.NestedType is not null).Select(ToRoute).OfType<PlannedRoute>().ToList();
            if (routes.Count > 0)
                return new PlannedFieldMapping(field.SchemaProperty, field.Alias, Routes: routes);
        }

        // A block-editor field without a nested plan is dropped: never wrapInType, never a
        // scalar mapping of a block model's ToString().
        return null;
    }

    /// <summary>Two kept element types bound the same field alias to different nested properties.</summary>
    private static bool SameFieldDifferentTargets(List<ElementNode> kept)
    {
        var byAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in kept)
        {
            foreach (var field in node.Fields)
            {
                if (field.SchemaProperty is null)
                    continue;

                if (byAlias.TryGetValue(field.Alias, out var existing))
                {
                    if (!string.Equals(existing, field.SchemaProperty, StringComparison.Ordinal))
                        return true;
                }
                else
                {
                    byAlias[field.Alias] = field.SchemaProperty;
                }
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private bool CanPlanNested(BlockElementPropertyInfo field, int depth)
        => field.NestedBlockElementTypes is { Count: > 0 } && depth + 1 <= _options.MaxBlockRouteDepth;

    private static bool AnsweredNone(IReadOnlyDictionary<string, SystemOneAnswer> answers, string id)
        => answers.TryGetValue(id, out var answer) && string.Equals(answer.Choice, JudgmentSession.None, StringComparison.Ordinal);

    /// <summary>The union of the inner element types' fields, de-duplicated by alias, first-declaring element wins.</summary>
    private static List<BlockElementPropertyInfo> UnionFields(IReadOnlyList<BlockElementTypeInfo> elements)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BlockElementPropertyInfo>();
        foreach (var element in elements)
        {
            foreach (var field in element.PropertyInfos)
            {
                if (seen.Add(field.Alias))
                    result.Add(field);
            }
        }

        return result;
    }

    /// <summary>The nested type's targets with the collection-valued ones it declares moved to the front.</summary>
    private static List<SchemaPropertyInfo> CollectionFirst(IReadOnlyList<SchemaPropertyInfo> targets)
    {
        var front = new List<SchemaPropertyInfo>();
        foreach (var name in CollectionTargets)
        {
            var hit = targets.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                front.Add(hit);
        }

        return front.Concat(targets.Where(t => !front.Contains(t))).ToList();
    }

    private static Dictionary<string, string> BuildCriteria(IReadOnlyList<SchemaPropertyInfo> targets, string noneDescription)
    {
        var criteria = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in targets)
        {
            var accepts = string.Join(" or ", p.AcceptedTypes);
            criteria[p.Name] = accepts.Length > 0 ? $"{p.Name} — accepts {accepts}" : p.Name;
        }

        criteria[JudgmentSession.None] = noneDescription;
        return criteria;
    }

    private static string DescribeElement(BlockElementTypeInfo element)
        => $"\"{DisplayName(element)}\" (fields: {string.Join(", ", element.PropertyInfos.Select(p => p.Alias))})";

    private static string DisplayName(BlockElementTypeInfo element)
        => string.IsNullOrEmpty(element.Name) ? element.Alias : element.Name;

    private static string DisplayName(BlockElementPropertyInfo field)
        => string.IsNullOrEmpty(field.Name) ? field.Alias : field.Name;

    /// <summary>A sanitised path made unique across the whole plan (two aliases can sanitise to the same id).</summary>
    private static string UniquePath(HashSet<string> paths, params string[] parts)
    {
        var id = JudgmentSession.Id(parts);
        var candidate = id;
        for (var n = 2; !paths.Add(candidate); n++)
            candidate = $"{id}_{n}";
        return candidate;
    }

    /// <summary>One element type at one depth: what it lands on, what it became, and its fields.</summary>
    private sealed class ElementNode(
        string path,
        BlockElementTypeInfo element,
        string parentSchemaType,
        string targetProperty,
        int depth,
        string context)
    {
        public string Path { get; } = path;

        public BlockElementTypeInfo Element { get; } = element;

        /// <summary>The Schema.org type whose property this list lands on (the page type at depth 0).</summary>
        public string ParentSchemaType { get; } = parentSchemaType;

        public string TargetProperty { get; } = targetProperty;

        public int Depth { get; } = depth;

        /// <summary>Prose locating the list for question text ("in the Umbraco Block List `sections`").</summary>
        public string Context { get; } = context;

        public bool Skipped { get; set; }

        public string? Root { get; set; }

        public string? NestedType { get; set; }

        public List<FieldNode> Fields { get; } = [];

        public string Describe()
        {
            var fields = Element.PropertyInfos.Select(p =>
                SchemeWeaverConstants.PropertyEditors.BlockEditorAliases.Contains(p.EditorAlias) ? $"{p.Alias} (a nested block list)" : p.Alias);
            return $"Each \"{DisplayName(Element)}\" block (`{Element.Alias}`, fields: {string.Join(", ", fields)}) {Context} "
                + $"will be emitted as a nested Schema.org object under {ParentSchemaType}.{TargetProperty}.";
        }
    }

    /// <summary>One field of an element type and everything decided about it.</summary>
    private sealed class FieldNode(BlockElementPropertyInfo field)
    {
        public BlockElementPropertyInfo Field { get; } = field;

        public string Alias => Field.Alias;

        public bool IsBlockEditor { get; } = SchemeWeaverConstants.PropertyEditors.BlockEditorAliases.Contains(field.EditorAlias);

        public string? SchemaProperty { get; set; }

        public string? WrapInType { get; set; }

        public string? ExtractAs { get; set; }

        public string? NestedContentProperty { get; set; }

        public List<ElementNode>? NestedElements { get; set; }
    }
}

/// <summary>
/// One nested Block List row to plan: its key (the page schema property, which is also the
/// question-id root), the list's alias, the type and property it lands on, and its element types.
/// </summary>
internal sealed record BlockListSubject(
    string Key,
    string ListAlias,
    string ParentSchemaType,
    string TargetProperty,
    IReadOnlyList<BlockElementTypeInfo> Elements);

/// <summary>
/// The outcome for one block row. <see cref="RequiresRoutes"/> is the code rule behind
/// <see cref="TypeSafeRoutesMode.Auto"/>: element types landed on different nested types, an
/// element has a planned nested block field, an element was skipped while others were kept,
/// or two elements bound the same field alias to different properties. When it is false the
/// list is expressible in the v1 shape (<see cref="NestedMappings"/> under
/// <see cref="NestedType"/>), which is emitted unchanged so single-element lists produce what
/// they always did.
/// </summary>
internal sealed record BlockRoutePlan(
    string Key,
    int KeptElements,
    int SkippedElements,
    bool RequiresRoutes,
    IReadOnlyList<PlannedRoute> Routes,
    IReadOnlyList<PlannedFieldMapping> NestedMappings,
    string? NestedType);
