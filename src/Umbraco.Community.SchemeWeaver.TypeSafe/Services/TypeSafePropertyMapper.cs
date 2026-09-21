using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services;

/// <summary>
/// The property mapper, ported from the eval harness's <c>eval/typesafe-mapper.mjs</c>.
/// Code owns the workflow, the rules and the vocabulary; Jev supplies only the semantic
/// judgments a name-matching heuristic cannot make.
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><description><b>Bind</b> — one Choice per content property over the schema type's
///   real property names plus <c>__none</c>. The model selects from a closed set, so it cannot
///   invent an alias or a schema property; this structurally removes the failure mode the AI
///   satellite needs a JSON-salvage routine to survive.</description></item>
///   <item><description><b>Shape</b> — for bound rows only: a Noul for "distinct entity or plain
///   value?" (the single most common way both the heuristic and a careless LLM go wrong is
///   wrapping a lone scalar in a pointless object shell), and for block lists whether the
///   blocks flatten to strings or become nested objects.</description></item>
///   <item><description><b>Descent</b> — the nested type by beam search down the type tree from
///   the property's declared range (<see cref="BeamSearch"/>), so an out-of-range nested type
///   is not expressible.</description></item>
///   <item><description><b>Inner</b> — each block field, or each content property that claimed a
///   complex schema property, chooses a property of the nested type.</description></item>
/// </list>
/// Everything else is code: which source types are structurally possible, the declared range,
/// media never becoming a complexType shell, claim collisions, resolver-config assembly, and
/// the merge with the heuristic's priors.
/// </remarks>
public sealed class TypeSafePropertyMapper : ITypeSafePropertyMapper
{
    private const string Nested = "nested";
    private const string StringList = "stringList";

    /// <summary>The API allows 255 options per Choice; one slot is reserved for <c>__none</c>.</summary>
    private const int HardOptionCap = 254;

    private static readonly JsonSerializerOptions ConfigJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ITypeSafeClient _client;
    private readonly ISchemaTypeGraph _graph;
    private readonly ISchemaTypeRegistry _registry;
    private readonly IContentTypeService _contentTypeService;
    private readonly IServiceProvider _serviceProvider;
    private readonly TypeSafeOptions _options;
    private readonly SchemaAutoMapperOptions _autoMapperOptions;
    private readonly ILogger<TypeSafePropertyMapper> _logger;

    public TypeSafePropertyMapper(
        ITypeSafeClient client,
        ISchemaTypeGraph graph,
        ISchemaTypeRegistry registry,
        IContentTypeService contentTypeService,
        IServiceProvider serviceProvider,
        IOptions<TypeSafeOptions> options,
        IOptions<SchemaAutoMapperOptions> autoMapperOptions,
        ILogger<TypeSafePropertyMapper> logger)
    {
        _client = client;
        _graph = graph;
        _registry = registry;
        _contentTypeService = contentTypeService;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _autoMapperOptions = autoMapperOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PropertyMappingSuggestion>> MapAsync(
        string contentTypeAlias,
        string schemaTypeName,
        IReadOnlyList<PropertyMappingSuggestion> priors,
        CancellationToken cancellationToken = default)
    {
        priors ??= [];

        var snapshot = await ContentTypeSnapshot
            .BuildAsync(_contentTypeService, _serviceProvider, contentTypeAlias, _logger, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            _logger.LogDebug("TypeSafe mapper: content type {ContentType} not found", contentTypeAlias);
            return [];
        }

        var schemaProperties = _registry.GetProperties(schemaTypeName).ToList();
        if (schemaProperties.Count == 0)
        {
            _logger.LogDebug("TypeSafe mapper: schema type {SchemaType} has no properties in the registry", schemaTypeName);
            return MergeWithPriors(priors, []);
        }

        var session = new JudgmentSession(_client, _options.MaxQuestionsPerRequest, _logger);
        var state = snapshot.ToState(schemaTypeName);

        var rows = await BindAsync(session, state, snapshot, schemaTypeName, schemaProperties, cancellationToken).ConfigureAwait(false);
        await ShapeAsync(session, state, rows, schemaTypeName, cancellationToken).ConfigureAwait(false);
        await DescendAsync(session, state, rows, schemaTypeName, cancellationToken).ConfigureAwait(false);
        var innerAnswers = await AskInnerAsync(session, state, rows, schemaTypeName, cancellationToken).ConfigureAwait(false);

        var typeSafeRows = Assemble(rows, innerAnswers);
        var result = MergeWithPriors(priors, typeSafeRows);

        _logger.LogInformation(
            "TypeSafe mapped {ContentType} to {SchemaType}: {Rows} row(s) emitted ({TypeSafeRows} from TypeSafe), {Requests} request(s), {Questions} question(s), {InputTokens} input tokens",
            contentTypeAlias, schemaTypeName, result.Count, typeSafeRows.Count, session.Requests, session.Questions, session.InputTokens);

        return result;
    }

    // ---------------------------------------------------------------------------
    // Round 1 — bind each content property to a schema property (or none)
    // ---------------------------------------------------------------------------

    private async Task<List<MappingRow>> BindAsync(
        JudgmentSession session,
        object state,
        ContentTypeSnapshot snapshot,
        string schemaTypeName,
        List<SchemaPropertyInfo> schemaProperties,
        CancellationToken cancellationToken)
    {
        var targets = BindTargets(schemaTypeName, schemaProperties);
        var criteria = BuildCriteria(targets, $"None of these — this content property has no good Schema.org target here.");
        var allowed = targets.Select(t => t.Name).ToList();

        var questions = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
        var idByAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in snapshot.Properties)
        {
            var id = UniqueId(questions, "bind", property.Alias);
            idByAlias[property.Alias] = id;

            var blockSummary = property.IsBlock ? snapshot.SummariseBlocks(property.Alias) : string.Empty;
            var blockGuidance = property.IsBlock
                ? " A repeating list of content like this is the page's structured body — it belongs on a "
                  + "property that holds a collection of things (the page's main entity, its parts, its list "
                  + "items), not left out. Judge it by what the blocks actually contain."
                : string.Empty;

            questions[id] = SystemOneQuestion.Choice(
                $"The Umbraco content property `{property.Alias}` (label \"{property.Name}\", editor {property.EditorAlias}) "
                + $"holds part of this page's content.{blockSummary} "
                + $"Which single property of the Schema.org type {schemaTypeName} should be populated from it?{blockGuidance} "
                + $"Choose {JudgmentSession.None} if none of the listed properties is a good semantic fit for what this "
                + "property actually holds.",
                criteria);
        }

        var answers = await session.AskChunkedAsync(state, questions, "bind", cancellationToken).ConfigureAwait(false);

        // schema property (case-insensitive) -> claimants, most confident first.
        var claims = new Dictionary<string, List<Claimant>>(StringComparer.OrdinalIgnoreCase);
        var claimOrder = new List<string>();
        foreach (var property in snapshot.Properties)
        {
            var id = idByAlias[property.Alias];
            var choice = JudgmentSession.Choice(answers, id, allowed);
            if (choice is null)
                continue;

            var confidence = JudgmentSession.Confidence(answers, id, choice);
            if (ToPercent(confidence) < _options.MinBindingConfidence)
                continue;

            if (!claims.TryGetValue(choice, out var claimants))
            {
                claims[choice] = claimants = [];
                claimOrder.Add(choice);
            }

            claimants.Add(new Claimant(property, confidence));
        }

        var rows = new List<MappingRow>();
        foreach (var schemaProp in claimOrder)
        {
            var reg = schemaProperties.First(p => string.Equals(p.Name, schemaProp, StringComparison.OrdinalIgnoreCase));
            var claimants = claims[schemaProp].OrderByDescending(c => c.Confidence).ToList();
            var primary = claimants[0];
            var isBlock = primary.Property.IsBlock;
            var innerProps = isBlock ? snapshot.BlockInnerProperties(primary.Property.Alias) : [];

            // RULE, not judgment: a media property resolves to a fully-populated ImageObject
            // through the media resolver, so it must stay `property` — wrapping it produces an
            // empty shell (the trap documented on SchemaAutoMapper's *.logo popular default).
            // A content picker likewise resolves to the picked node's own entity through the
            // picked-content ladder, which the heuristic also keeps as `property`.
            var canBeComplex = !isBlock
                && !primary.Property.IsMedia
                && !primary.Property.IsContentPicker
                && reg.IsComplexType
                && _graph.RangeOf(schemaTypeName, reg.Name).Count > 0;

            rows.Add(new MappingRow(reg, primary, isBlock, innerProps, canBeComplex)
            {
                // Several content properties can legitimately assemble ONE nested entity
                // (locationName + locationAddress -> Place). For a scalar target only the most
                // confident claim survives; for a nested entity they are all source fields.
                Claimants = canBeComplex || isBlock ? claimants : [primary],
                SourceType = isBlock ? SchemeWeaverConstants.SourceTypes.BlockContent : SchemeWeaverConstants.SourceTypes.Property,
            });
        }

        _logger.LogDebug("TypeSafe bind: {Bound} of {Total} content properties bound to {Distinct} schema properties",
            claims.Values.Sum(c => c.Count), snapshot.Properties.Count, rows.Count);

        return rows;
    }

    // ---------------------------------------------------------------------------
    // Round 2 — source type / shape
    // ---------------------------------------------------------------------------

    private async Task ShapeAsync(
        JudgmentSession session,
        object state,
        List<MappingRow> rows,
        string schemaTypeName,
        CancellationToken cancellationToken)
    {
        var questions = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
        var shapeIds = new Dictionary<MappingRow, string>();
        var entityIds = new Dictionary<MappingRow, string>();

        foreach (var row in rows)
        {
            if (row.IsBlock)
            {
                if (row.InnerProps.Count > 1)
                {
                    var id = UniqueId(questions, "shape", row.SchemaProperty.Name);
                    shapeIds[row] = id;
                    questions[id] = SystemOneQuestion.Choice(
                        $"The Umbraco Block List property `{row.Primary.Property.Alias}` supplies {schemaTypeName}.{row.SchemaProperty.Name}. "
                        + $"Each block holds these fields: {string.Join(", ", row.InnerProps.Select(p => $"`{p.Alias}`"))}. "
                        + "Should each block become one nested Schema.org object carrying several of those fields, "
                        + "or should the blocks flatten to a plain list of text values?",
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [Nested] = "Each block is a distinct thing with several meaningful fields (a step, a question and its answer, a team member).",
                            [StringList] = "Each block carries essentially one meaningful label, so the list is just a list of strings (ingredients, tools, tags).",
                        });
                }

                continue;
            }

            if (row.CanBeComplex)
            {
                var id = UniqueId(questions, "entity", row.SchemaProperty.Name);
                entityIds[row] = id;
                var claimants = string.Join(", ", row.Claimants.Select(c => $"`{c.Property.Alias}`"));
                var plural = row.Claimants.Count > 1 ? "properties" : "property";
                questions[id] = SystemOneQuestion.Noul(
                    $"The Umbraco {plural} {claimants} supply {schemaTypeName}.{row.SchemaProperty.Name}. "
                    + $"Does {row.SchemaProperty.Name} denote a distinct named entity — a person, organisation, place or similar "
                    + "thing that deserves its own nested object with its own properties — rather than a plain value that "
                    + "should be written straight out?",
                    new NoulCriteria(
                        $"{row.SchemaProperty.Name} names a thing in its own right, so it should be emitted as a nested Schema.org object with its own properties.",
                        $"{row.SchemaProperty.Name} is a plain value (text, a number, a date, a URL, an image) and should be written straight out as a scalar."));
            }
        }

        var answers = await session.AskChunkedAsync(state, questions, "shape", cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (row.IsBlock)
            {
                if (row.InnerProps.Count <= 1)
                {
                    row.Shape = StringList;
                }
                else
                {
                    var choice = JudgmentSession.Choice(answers, shapeIds[row], [Nested, StringList]);
                    row.Shape = string.Equals(choice, StringList, StringComparison.Ordinal) ? StringList : Nested;
                }

                row.NeedsNestedType = row.Shape == Nested;
                continue;
            }

            if (row.CanBeComplex && JudgmentSession.Noul(answers, entityIds[row]) >= 0.5)
            {
                row.SourceType = SchemeWeaverConstants.SourceTypes.ComplexType;
                row.NeedsNestedType = true;
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Round 2b — nested type descent (hierarchical classification, beam search)
    // ---------------------------------------------------------------------------

    private async Task DescendAsync(
        JudgmentSession session,
        object state,
        List<MappingRow> rows,
        string schemaTypeName,
        CancellationToken cancellationToken)
    {
        var active = new List<(MappingRow Row, IReadOnlyList<string> Roots)>();
        foreach (var row in rows.Where(r => r.NeedsNestedType))
        {
            var roots = _graph.RangeOf(schemaTypeName, row.SchemaProperty.Name);
            if (roots.Count > 0)
                active.Add((row, roots));
        }

        if (active.Count > 0)
        {
            // Level 0: which declared range root, when a property accepts more than one.
            var rootQuestions = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
            var rootIds = new Dictionary<MappingRow, string>();
            var start = new Dictionary<MappingRow, string>();
            foreach (var (row, roots) in active)
            {
                if (roots.Count == 1)
                {
                    start[row] = roots[0];
                    continue;
                }

                var id = UniqueId(rootQuestions, "root", row.SchemaProperty.Name);
                rootIds[row] = id;
                var criteria = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var root in roots.Take(HardOptionCap))
                    criteria[root] = $"It is a {root} (or a more specific kind of {root}).";

                rootQuestions[id] = SystemOneQuestion.Choice(
                    $"{DescribeSubject(row, schemaTypeName)} Which of these Schema.org types is the right family for it?",
                    criteria);
            }

            if (rootQuestions.Count > 0)
            {
                var rootAnswers = await session.AskChunkedAsync(state, rootQuestions, "root", cancellationToken).ConfigureAwait(false);
                foreach (var (row, roots) in active)
                {
                    if (start.ContainsKey(row))
                        continue;

                    start[row] = JudgmentSession.Choice(rootAnswers, rootIds[row], roots) ?? roots[0];
                }
            }

            var subjects = active
                .Select(a => new DescentSubject(a.Row.SchemaProperty.Name, start[a.Row], DescribeSubject(a.Row, schemaTypeName)))
                .ToList();

            var beamSearch = new BeamSearch(session, _graph, _options.MaxOptionsPerChoice, _logger);
            var beams = await beamSearch
                .DescendAsync(state, subjects, _options.BeamWidth, _options.MaxDescentDepth, cancellationToken)
                .ConfigureAwait(false);

            foreach (var (row, _) in active)
            {
                // The winning beam's geometric mean is logged by BeamSearch but does not feed
                // the row's confidence: the eval gated on the bind answer alone, and changing
                // the formula here would change gating without a measurement behind it.
                var winner = beams.TryGetValue(row.SchemaProperty.Name, out var ranked) && ranked.Count > 0 ? ranked[0] : null;
                row.NestedType = winner?.Type ?? start[row];
            }
        }

        // No usable range: fall back to the shape that needs no nested type.
        foreach (var row in rows.Where(r => r.NeedsNestedType && r.NestedType is null))
        {
            if (row.IsBlock)
                row.Shape = StringList;
            else
                row.SourceType = SchemeWeaverConstants.SourceTypes.Property;
            row.NeedsNestedType = false;
        }
    }

    /// <summary>A one-line description of what a rich row's nested objects actually represent.</summary>
    private static string DescribeSubject(MappingRow row, string schemaTypeName)
    {
        if (row.IsBlock)
        {
            var blockName = row.InnerProps.Count > 0 ? row.InnerProps[0].BlockName : row.Primary.Property.Alias;
            return $"Each block of the Umbraco Block List `{row.Primary.Property.Alias}` (block type \"{blockName}\", "
                + $"fields: {string.Join(", ", row.InnerProps.Select(p => p.Alias))}) will be emitted as a nested Schema.org "
                + $"object under {schemaTypeName}.{row.SchemaProperty.Name}.";
        }

        var claimants = string.Join(", ", row.Claimants.Select(c => $"`{c.Property.Alias}` (label \"{c.Property.Name}\")"));
        var plural = row.Claimants.Count > 1 ? "properties" : "property";
        return $"The Umbraco {plural} {claimants} will be assembled into one nested Schema.org object under "
            + $"{schemaTypeName}.{row.SchemaProperty.Name}.";
    }

    // ---------------------------------------------------------------------------
    // Round 3 — inner bindings
    // ---------------------------------------------------------------------------

    private async Task<InnerAnswers> AskInnerAsync(
        JudgmentSession session,
        object state,
        List<MappingRow> rows,
        string schemaTypeName,
        CancellationToken cancellationToken)
    {
        var questions = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
        var inner = new InnerAnswers();

        foreach (var row in rows)
        {
            if (row.SourceType == SchemeWeaverConstants.SourceTypes.BlockContent && row.Shape == Nested && row.NestedType is not null)
            {
                var targets = NestedTargets(row.NestedType);
                inner.NestedAllowed[row] = targets.Select(t => t.Name).ToList();
                var criteria = BuildCriteria(targets, $"None of these — this field has no good {row.NestedType} property.");
                foreach (var ip in row.InnerProps)
                {
                    var id = UniqueId(questions, "inner", row.SchemaProperty.Name, ip.Alias);
                    inner.Ids[(row, ip.Alias)] = id;
                    questions[id] = SystemOneQuestion.Choice(
                        $"Inside the block `{ip.BlockAlias}`, the field `{ip.Alias}` (label \"{ip.Name}\", editor {ip.EditorAlias}) "
                        + $"holds content. Each block is emitted as a Schema.org {row.NestedType}. "
                        + $"Which property of {row.NestedType} should this field populate?",
                        criteria);
                }
            }

            if (row.SourceType == SchemeWeaverConstants.SourceTypes.BlockContent && row.Shape == StringList && row.InnerProps.Count > 1)
            {
                var id = UniqueId(questions, "strlist", row.SchemaProperty.Name);
                inner.StringListIds[row] = id;
                var criteria = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var ip in row.InnerProps.Take(HardOptionCap + 1))
                    criteria[ip.Alias] = $"The `{ip.Alias}` field (label \"{ip.Name}\", {ip.EditorAlias}).";

                questions[id] = SystemOneQuestion.Choice(
                    $"The blocks in `{row.Primary.Property.Alias}` flatten to a plain list of text values for "
                    + $"{schemaTypeName}.{row.SchemaProperty.Name}. Which single field of the block carries the text "
                    + "that should appear in that list?",
                    criteria);
            }

            if (row.SourceType == SchemeWeaverConstants.SourceTypes.ComplexType && row.NestedType is not null)
            {
                var targets = NestedTargets(row.NestedType);
                inner.NestedAllowed[row] = targets.Select(t => t.Name).ToList();
                var criteria = BuildCriteria(targets, $"None of these — this field has no good {row.NestedType} property.");
                foreach (var claimant in row.Claimants)
                {
                    var id = UniqueId(questions, "inner", row.SchemaProperty.Name, claimant.Property.Alias);
                    inner.Ids[(row, claimant.Property.Alias)] = id;
                    questions[id] = SystemOneQuestion.Choice(
                        $"{schemaTypeName}.{row.SchemaProperty.Name} is emitted as a nested {row.NestedType}. The Umbraco "
                        + $"property `{claimant.Property.Alias}` (label \"{claimant.Property.Name}\", editor {claimant.Property.EditorAlias}) is one of "
                        + $"its source fields. Which property of {row.NestedType} should that value populate?",
                        criteria);
                }
            }
        }

        inner.Answers = await session.AskChunkedAsync(state, questions, "inner", cancellationToken).ConfigureAwait(false);
        return inner;
    }

    // ---------------------------------------------------------------------------
    // Assembly
    // ---------------------------------------------------------------------------

    private List<PropertyMappingSuggestion> Assemble(List<MappingRow> rows, InnerAnswers inner)
    {
        var suggestions = new List<PropertyMappingSuggestion>();
        foreach (var row in rows)
        {
            var suggestion = new PropertyMappingSuggestion
            {
                SchemaPropertyName = row.SchemaProperty.Name,
                SchemaPropertyType = row.SchemaProperty.PropertyType,
                SuggestedContentTypePropertyAlias = row.Primary.Property.Alias,
                SuggestedSourceType = row.SourceType,
                SuggestedNestedSchemaTypeName = row.NestedType,
                EditorAlias = row.Primary.Property.EditorAlias,
                AcceptedTypes = row.SchemaProperty.AcceptedTypes,
                IsComplexType = row.SchemaProperty.IsComplexType,
                Confidence = ToPercent(row.Primary.Confidence),
            };

            if (row.SourceType == SchemeWeaverConstants.SourceTypes.BlockContent && row.InnerProps.Count == 0)
            {
                // The model bound the block but its element types could not be introspected
                // (the snapshot degraded to "a block, described by name"). Keep the row as a
                // plain blockContent suggestion with no config: the core's BlockContentResolver
                // maps block fields by name when no config is present, which is what the
                // heuristic emits in the same situation. Dropping it would lose a binding the
                // model made.
                suggestion.SuggestedNestedSchemaTypeName = null;
                suggestions.Add(suggestion);
                continue;
            }

            if (row.SourceType == SchemeWeaverConstants.SourceTypes.BlockContent && row.Shape == StringList)
            {
                string? pick;
                if (row.InnerProps.Count == 1)
                {
                    pick = row.InnerProps[0].Alias;
                }
                else
                {
                    var allowed = row.InnerProps.Select(p => p.Alias).ToList();
                    pick = (inner.StringListIds.TryGetValue(row, out var id) ? JudgmentSession.Choice(inner.Answers, id, allowed) : null)
                        ?? allowed.FirstOrDefault();
                }

                if (pick is null)
                    continue;

                suggestion.SuggestedNestedSchemaTypeName = null;
                suggestion.SuggestedResolverConfig = JsonSerializer.Serialize(
                    new { extractAs = StringList, contentProperty = pick }, ConfigJson);
            }

            if (row.SourceType == SchemeWeaverConstants.SourceTypes.BlockContent && row.Shape == Nested && row.NestedType is not null)
            {
                // NOT de-duplicated by schema property: a Block List usually allows several element
                // types, and two of them can legitimately feed the same nested property
                // (heroBlock.heroImage and featureBlock.featureImage both supply Image). Dropping
                // the second would silently lose one block type's content.
                var allowed = inner.NestedAllowed[row];
                var nestedMappings = new List<object>();
                foreach (var ip in row.InnerProps)
                {
                    var choice = inner.Ids.TryGetValue((row, ip.Alias), out var id) ? JudgmentSession.Choice(inner.Answers, id, allowed) : null;
                    if (choice is null)
                        continue;

                    // If the chosen nested property is itself an entity, the value has to be wrapped
                    // (Question.acceptedAnswer -> Answer.text). Which type to wrap into is a RULE taken
                    // from the registry's declared range for that nested property. Which property of
                    // the wrapper receives the value is deliberately NOT set: the core's
                    // BlockContentResolver infers it at render time from the field name (exact, then
                    // partial match against the wrapper's properties, falling back to Text) whenever
                    // wrapInProperty is empty, and a fixed Name/Text here would override that with
                    // wrong shapes such as Rating.name where Rating.ratingValue is meant.
                    var wrapType = _graph.RangeOf(row.NestedType, choice).FirstOrDefault();
                    nestedMappings.Add(new
                    {
                        schemaProperty = choice,
                        contentProperty = ip.Alias,
                        wrapInType = wrapType,
                    });
                }

                if (nestedMappings.Count == 0)
                    continue;

                suggestion.SuggestedResolverConfig = JsonSerializer.Serialize(new { nestedMappings }, ConfigJson);
            }

            if (row.SourceType == SchemeWeaverConstants.SourceTypes.ComplexType && row.NestedType is not null)
            {
                // De-duplicated: one entity, one value per nested property.
                var allowed = inner.NestedAllowed[row];
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var complexTypeMappings = new List<object>();
                foreach (var claimant in row.Claimants)
                {
                    var choice = inner.Ids.TryGetValue((row, claimant.Property.Alias), out var id) ? JudgmentSession.Choice(inner.Answers, id, allowed) : null;
                    if (choice is null || !used.Add(choice))
                        continue;

                    complexTypeMappings.Add(new
                    {
                        schemaProperty = choice,
                        sourceType = SchemeWeaverConstants.SourceTypes.Property,
                        contentTypePropertyAlias = claimant.Property.Alias,
                    });
                }

                if (complexTypeMappings.Count == 0)
                    continue;

                suggestion.SuggestedResolverConfig = JsonSerializer.Serialize(new { complexTypeMappings }, ConfigJson);
            }

            suggestions.Add(suggestion);
        }

        return suggestions;
    }

    // ---------------------------------------------------------------------------
    // Priors merge + gating
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Combines TypeSafe's rows with the heuristic's suggestions (the priors) according to
    /// <see cref="TypeSafeOptions.PriorsMode"/>, then gates exactly as the heuristic does
    /// (<c>IsAutoMapped</c> at the auto-apply bar, rows below the show bar dropped).
    /// </summary>
    /// <remarks>
    /// This is a design decision that was MEASURED, not an eval finding: the harness scores
    /// TypeSafe on its own, so every merge rule was scored afterwards from the same answers
    /// (<c>eval/run-typesafe.mjs</c>, the "with heuristic priors" block). The rule this package
    /// first shipped with, keeping the heuristic's non-<c>property</c> rows at or above the
    /// auto-apply bar and letting them override TypeSafe, halved rich coverage (4 of 12 against
    /// 8 of 12) because the kept rows carried wrong shapes that pre-empted correct ones. Gap
    /// filling, where a prior is added only for a schema property TypeSafe left unmapped,
    /// recovered one more rich row (the Google-mandated <c>FAQPage.mainEntity</c> block shape)
    /// but cost precision (strict F1 0.569 against 0.714), because the heuristic's shown-but-weak
    /// rows come along with it. So <see cref="TypeSafePriorsMode.None"/> is the default and
    /// <see cref="TypeSafePriorsMode.GapFill"/> is opt-in, restricted to the heuristic's
    /// rule-driven rows (anything not a plain <c>property</c> match, or an exact-alias 100).
    /// The measurement used the cached June baseline, which predates the core's range-aware
    /// enricher uplift; re-run it before promoting GapFill.
    /// </remarks>
    private List<PropertyMappingSuggestion> MergeWithPriors(
        IReadOnlyList<PropertyMappingSuggestion> priors,
        IReadOnlyList<PropertyMappingSuggestion> typeSafeRows)
    {
        var autoApply = _autoMapperOptions.AutoApplyConfidenceThreshold;
        var show = _autoMapperOptions.ShowConfidenceThreshold;

        var merged = typeSafeRows.OrderByDescending(r => r.Confidence).ToList();
        var filled = 0;

        if (_options.PriorsMode == TypeSafePriorsMode.GapFill)
        {
            var bound = new HashSet<string>(merged.Select(r => r.SchemaPropertyName), StringComparer.OrdinalIgnoreCase);
            foreach (var prior in priors)
            {
                var ruleShaped = !string.Equals(prior.SuggestedSourceType, SchemeWeaverConstants.SourceTypes.Property, StringComparison.OrdinalIgnoreCase)
                    || prior.Confidence == 100;
                if (ruleShaped && bound.Add(prior.SchemaPropertyName))
                {
                    merged.Add(prior);
                    filled++;
                }
            }
        }

        foreach (var suggestion in merged)
            suggestion.IsAutoMapped = suggestion.Confidence >= autoApply;

        var result = merged.Where(s => s.Confidence >= show).ToList();

        _logger.LogDebug("TypeSafe merge ({Mode}): {TypeSafe} TypeSafe row(s), {Filled} prior(s) gap-filled of {Priors}, {Result} after gating",
            _options.PriorsMode, typeSafeRows.Count, filled, priors.Count, result.Count);

        return result;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Binding targets for the top-level bind: ALL of the schema type's properties, in the
    /// heuristic's ranked order (popular-for-type first, then globally popular, then the rest),
    /// capped at the Choice limit. The ORDER is deliberate: the eval offered the ranked list,
    /// and the first live run of this port, which offered registry order (<c>MainEntity</c>
    /// 76th of 129 on <c>FAQPage</c> rather than 2nd, <c>RecipeInstructions</c> 6th rather than
    /// 3rd), bound the same block properties noticeably less confidently. The ranking never
    /// pre-filters below the cap, which would cap recall.
    /// </summary>
    private List<SchemaPropertyInfo> BindTargets(string schemaTypeName, List<SchemaPropertyInfo> schemaProperties)
    {
        var cap = Math.Clamp(_options.MaxOptionsPerChoice, 1, HardOptionCap);
        var byName = new Dictionary<string, SchemaPropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in schemaProperties)
            byName.TryAdd(p.Name, p);

        var ordered = new List<SchemaPropertyInfo>(schemaProperties.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ranked in Heuristic().RankSchemaProperties(schemaTypeName))
        {
            if (byName.TryGetValue(ranked.Name, out var p) && seen.Add(p.Name))
                ordered.Add(p);
        }

        // Anything the ranking did not mention keeps its registry order at the end.
        foreach (var p in schemaProperties)
        {
            if (seen.Add(p.Name))
                ordered.Add(p);
        }

        return ordered.Count <= cap ? ordered : ordered.Take(cap).ToList();
    }

    /// <summary>
    /// Inner-binding targets for a nested type: registry order, exactly as the eval offered
    /// them; the ranking is consulted only to decide what to drop if a type exceeds the cap.
    /// </summary>
    private List<SchemaPropertyInfo> NestedTargets(string nestedType)
    {
        var schemaProperties = _registry.GetProperties(nestedType).ToList();
        var cap = Math.Clamp(_options.MaxOptionsPerChoice, 1, HardOptionCap);
        if (schemaProperties.Count <= cap)
            return schemaProperties;

        var ranked = Heuristic()
            .RankSchemaProperties(nestedType)
            .Select(p => p.Name)
            .Take(cap)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return schemaProperties.Where(p => ranked.Contains(p.Name)).Take(cap).ToList();
    }

    /// <summary>
    /// The core heuristic, for its property ranking (popular-for-type > globally popular >
    /// complex > rest), reused rather than re-implemented so the two mappers can never disagree
    /// about it. The composer registers the concrete type precisely so it can be resolved here;
    /// the hand-built fallback only covers a host that composed without it.
    /// </summary>
    private SchemaAutoMapper Heuristic()
        => _serviceProvider.GetService<SchemaAutoMapper>() ?? new SchemaAutoMapper(_contentTypeService, _registry);

    /// <summary>Option label for a schema property: its name plus what it accepts, then the none option last.</summary>
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

    /// <summary>A sanitised question id, made unique within the round (two aliases can sanitise to the same id).</summary>
    private static string UniqueId(Dictionary<string, SystemOneQuestion> questions, params string[] parts)
    {
        var id = JudgmentSession.Id(parts);
        var candidate = id;
        for (var n = 2; questions.ContainsKey(candidate); n++)
            candidate = $"{id}_{n}";
        return candidate;
    }

    private static int ToPercent(double probability)
        => Math.Clamp((int)Math.Round(probability * 100, MidpointRounding.AwayFromZero), 0, 100);

    /// <summary>One content property's claim on a schema property, with the calibrated confidence of the bind answer.</summary>
    private sealed record Claimant(SnapshotProperty Property, double Confidence);

    /// <summary>One schema property that at least one content property claimed, and everything decided about it since.</summary>
    private sealed class MappingRow(
        SchemaPropertyInfo schemaProperty,
        Claimant primary,
        bool isBlock,
        IReadOnlyList<BlockInnerProperty> innerProps,
        bool canBeComplex)
    {
        public SchemaPropertyInfo SchemaProperty { get; } = schemaProperty;

        public Claimant Primary { get; } = primary;

        public IReadOnlyList<Claimant> Claimants { get; init; } = [primary];

        public bool IsBlock { get; } = isBlock;

        public IReadOnlyList<BlockInnerProperty> InnerProps { get; } = innerProps;

        public bool CanBeComplex { get; } = canBeComplex;

        public string SourceType { get; set; } = SchemeWeaverConstants.SourceTypes.Property;

        /// <summary><c>nested</c> or <c>stringList</c> for a block row; null otherwise.</summary>
        public string? Shape { get; set; }

        public string? NestedType { get; set; }

        public bool NeedsNestedType { get; set; }
    }

    private sealed class InnerAnswers
    {
        public Dictionary<string, SystemOneAnswer> Answers { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<(MappingRow Row, string Alias), string> Ids { get; } = [];

        public Dictionary<MappingRow, string> StringListIds { get; } = [];

        public Dictionary<MappingRow, IReadOnlyList<string>> NestedAllowed { get; } = [];
    }
}
