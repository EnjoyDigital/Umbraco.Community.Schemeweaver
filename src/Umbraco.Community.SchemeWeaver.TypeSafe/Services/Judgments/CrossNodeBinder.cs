using Microsoft.Extensions.Logging;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

/// <summary>
/// The cross-node round: for schema properties no property of the page itself claimed, one
/// Choice each over every compatible property of the page's neighbours (parents, ancestors,
/// siblings) plus <c>__none</c>, producing <c>parent</c>/<c>ancestor</c>/<c>sibling</c> rows.
/// Runs against a second request state that carries the neighbourhood, so its tokens are paid
/// only here and not on every bind, shape, descent and inner request.
/// </summary>
/// <remarks>
/// <para>
/// Code owns three things the model must not decide. WHICH properties are asked about: a
/// self-only set (<see cref="SelfOnlySchemaProperties"/>) is never asked, because a page's own
/// headline, body, dates or canonical URL read from a related page is wrong on every page of
/// the type, however plausible one neighbour's property looks; the rest are ordered
/// cross-node-prone first (<see cref="CrossNodeProneProperties"/>: the categories, publishers,
/// organisers and brands a listing or a site root genuinely supplies) so the question cap
/// spends on the likely wins. WHICH options are offered: an editor/range filter keeps a date
/// off a text property and a media picker off <c>category</c>, so an option the model cannot
/// pick correctly is not on the list. And the bar: <see cref="TypeSafeOptions.MinCrossNodeConfidence"/>
/// defaults to the core's show bar, so a cross-node row is shown like any other suggestion and
/// pre-ticked only at the core's auto-apply bar; the measured rows an editor wants to review sit
/// between the two.
/// </para>
/// <para>
/// Option keys are code-generated (<c>parent__productListing__title</c>) and decoded by
/// dictionary lookup, never parsed: an alias with a double underscore in it cannot corrupt the
/// decode. <c>__none</c> is listed FIRST and framed as the default, the measured position for
/// a "no" default. Every alias in an emitted row comes from the neighbourhood, which is read
/// from real Umbraco structure.
/// </para>
/// </remarks>
internal sealed class CrossNodeBinder
{
    /// <summary>The API allows 255 options per Choice; one slot is reserved for <c>__none</c>.</summary>
    private const int HardOptionCap = 254;

    /// <summary>
    /// Schema properties that must come from the page itself. Never asked in this round even
    /// when unbound locally: a related page's value for any of them is wrong by construction.
    /// </summary>
    internal static readonly HashSet<string> SelfOnlySchemaProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "headline", "alternateName", "alternativeHeadline", "description", "abstract", "url",
        "identifier", "image", "thumbnailUrl", "mainEntityOfPage", "text", "articleBody",
        "dateCreated", "dateModified", "datePublished", "sameAs",
    };

    /// <summary>
    /// Schema properties a related node most often supplies, asked first so the question cap
    /// (<see cref="TypeSafeOptions.MaxCrossNodeQuestions"/>) is spent where a cross-node row is
    /// likely to be right. In priority order.
    /// </summary>
    internal static readonly string[] CrossNodeProneProperties =
    [
        "category", "publisher", "parentOrganization", "isPartOf", "provider", "organizer", "location",
        "brand", "manufacturer", "sourceOrganization", "containedInPlace", "memberOf", "worksFor",
        "affiliation", "genre", "keywords", "inLanguage", "copyrightHolder", "funder", "sponsor",
        "areaServed", "address", "telephone", "email", "logo",
    ];

    private const string DateTimeEditor = "Umbraco.DateTime";
    private const string MultiUrlPickerEditor = "Umbraco.MultiUrlPicker";
    private const string IntegerEditor = "Umbraco.Integer";
    private const string DecimalEditor = "Umbraco.Decimal";

    private readonly JudgmentSession _session;
    private readonly ISchemaTypeGraph _graph;
    private readonly TypeSafeOptions _options;
    private readonly ILogger _logger;

    public CrossNodeBinder(JudgmentSession session, ISchemaTypeGraph graph, TypeSafeOptions options, ILogger logger)
    {
        _session = session;
        _graph = graph;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Asks and assembles the cross-node rows. Returns an empty list when the neighbourhood is
    /// empty, when nothing is left to ask, or when no answer clears the bar.
    /// <paramref name="claimedLocally"/> holds the schema properties a page property claimed at
    /// ANY confidence in the bind round; <paramref name="rankedOrder"/> is the heuristic's
    /// ranked property order for the schema type, consulted after the prone list.
    /// </summary>
    public async Task<List<PropertyMappingSuggestion>> BindAsync(
        ContentTypeSnapshot snapshot,
        string schemaTypeName,
        IReadOnlyList<SchemaPropertyInfo> schemaProperties,
        IReadOnlySet<string> claimedLocally,
        IReadOnlyList<string> rankedOrder,
        CancellationToken cancellationToken)
    {
        var neighbourhood = snapshot.Neighbourhood;
        if (neighbourhood.IsEmpty)
            return [];

        var candidates = Candidates(schemaProperties, claimedLocally, rankedOrder);
        if (candidates.Count == 0)
            return [];

        var offered = Offer(neighbourhood);
        if (offered.Count == 0)
            return [];

        var questions = new Dictionary<string, SystemOneQuestion>(StringComparer.Ordinal);
        var asked = new List<(SchemaPropertyInfo Property, string Id, IReadOnlyList<string> Keys)>();
        foreach (var property in candidates)
        {
            var compatible = offered.Where(o => IsCompatible(o.Value.Property, property)).ToList();
            if (compatible.Count == 0)
                continue;

            var criteria = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [JudgmentSession.None] = $"No related page supplies {property.Name} for pages of this type; leave it unmapped.",
            };
            foreach (var (key, option) in compatible)
                criteria[key] = option.Description;

            var accepts = string.Join(" or ", property.AcceptedTypes);
            var id = JudgmentSession.Id("cross", property.Name);
            questions[id] = SystemOneQuestion.Choice(
                $"The Schema.org property {schemaTypeName}.{property.Name}"
                + (accepts.Length > 0 ? $" (accepts {accepts})" : string.Empty)
                + " is not supplied by any property of this page itself; which single property of a related page "
                + "(see neighbourhood) should populate it for EVERY page of this type? "
                + $"Choose {JudgmentSession.None} unless the related page's property clearly carries this value for the page. "
                + "Two patterns are common and correct: a grouping property (category, articleSection, genre, "
                + "keywords) is the title or name of the listing page the pages sit under, because that page IS the "
                + "group; and the organisation page above the site (its name, logo, contact details) is the publisher, "
                + "provider, source organisation, copyright holder or parent organisation of its pages, though not "
                + "their author, sponsor or funder.",
                criteria);
            asked.Add((property, id, compatible.Select(c => c.Key).ToList()));
        }

        if (asked.Count == 0)
            return [];

        var state = snapshot.ToState(schemaTypeName, neighbourhood);
        var answers = await _session.AskChunkedAsync(state, questions, "cross", cancellationToken).ConfigureAwait(false);

        var rows = new List<PropertyMappingSuggestion>();
        var belowBar = 0;
        foreach (var (property, id, keys) in asked)
        {
            var choice = JudgmentSession.Choice(answers, id, keys);
            if (choice is null || !offered.TryGetValue(choice, out var option))
                continue;

            var confidence = ToPercent(JudgmentSession.Confidence(answers, id, choice));
            if (confidence < _options.MinCrossNodeConfidence)
            {
                belowBar++;
                continue;
            }

            rows.Add(new PropertyMappingSuggestion
            {
                SchemaPropertyName = property.Name,
                SchemaPropertyType = property.PropertyType,
                SuggestedContentTypePropertyAlias = option.Property.Alias,
                SuggestedSourceType = SourceTypeFor(option.Type, neighbourhood),
                SuggestedSourceContentTypeAlias = option.Type.Alias,
                EditorAlias = option.Property.EditorAlias,
                AcceptedTypes = property.AcceptedTypes,
                IsComplexType = property.IsComplexType,
                Confidence = confidence,
            });
        }

        _logger.LogDebug("TypeSafe cross-node: {Asked} of {Candidates} candidate(s) asked over {Options} neighbour propert(ies); {Rows} row(s) kept, {BelowBar} below the {Bar} bar",
            asked.Count, candidates.Count, offered.Count, rows.Count, belowBar, _options.MinCrossNodeConfidence);

        return rows;
    }

    /// <summary>
    /// Unbound, not self-only, prone-first then the heuristic's ranked order then registry
    /// order, capped at <see cref="TypeSafeOptions.MaxCrossNodeQuestions"/>.
    /// </summary>
    private List<SchemaPropertyInfo> Candidates(
        IReadOnlyList<SchemaPropertyInfo> schemaProperties,
        IReadOnlySet<string> claimedLocally,
        IReadOnlyList<string> rankedOrder)
    {
        var byName = new Dictionary<string, SchemaPropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in schemaProperties)
        {
            if (!claimedLocally.Contains(p.Name) && !SelfOnlySchemaProperties.Contains(p.Name))
                byName.TryAdd(p.Name, p);
        }

        var ordered = new List<SchemaPropertyInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in CrossNodeProneProperties.Concat(rankedOrder).Concat(schemaProperties.Select(p => p.Name)))
        {
            if (byName.TryGetValue(name, out var p) && seen.Add(p.Name))
                ordered.Add(p);
        }

        var cap = Math.Max(0, _options.MaxCrossNodeQuestions);
        return ordered.Count <= cap ? ordered : ordered.Take(cap).ToList();
    }

    /// <summary>
    /// Every offerable neighbour property keyed by a code-generated option key, in the
    /// neighbourhood's option order (parents, ancestors nearest-first, siblings), capped at
    /// <see cref="TypeSafeOptions.MaxNeighbourProperties"/>. Block editors are left out: a
    /// repeating list on a related page is never one scalar value of this page.
    /// </summary>
    private Dictionary<string, NeighbourOption> Offer(ContentTypeNeighbourhood neighbourhood)
    {
        var cap = Math.Clamp(_options.MaxNeighbourProperties, 0, HardOptionCap);
        var offered = new Dictionary<string, NeighbourOption>(StringComparer.Ordinal);
        foreach (var type in neighbourhood.All)
        {
            var relation = type.Relation == NeighbourRelation.Ancestor
                ? $"ancestor page type (depth {type.Depth})"
                : $"{type.SourceType} page type";

            foreach (var property in type.Properties)
            {
                if (offered.Count >= cap)
                    return offered;

                if (SchemeWeaverConstants.PropertyEditors.BlockEditorAliases.Contains(property.EditorAlias))
                    continue;

                var key = UniqueKey(offered, type.SourceType, type.Alias, property.Alias);
                offered[key] = new NeighbourOption(
                    type,
                    property,
                    $"The {relation} \"{type.Name}\" (`{type.Alias}`): its `{property.Alias}` property "
                    + $"(label \"{property.Name}\", editor {property.EditorAlias}).");
            }
        }

        return offered;
    }

    /// <summary>
    /// The source type a chosen neighbour becomes. A <c>parent</c> row reads whichever page is
    /// the actual parent and the core ignores the type alias on it, so when the neighbourhood
    /// offers more than one parent type the chosen parent is emitted as an <c>ancestor</c> row
    /// instead: that honours the alias and, because Umbraco walks ancestors nearest-first,
    /// reads the parent itself when the parent is that type and never a parent of another
    /// type. A unique parent type keeps <c>parent</c>, the cheaper read.
    /// </summary>
    private static string SourceTypeFor(NeighbourType type, ContentTypeNeighbourhood neighbourhood)
        => type.Relation == NeighbourRelation.Parent && neighbourhood.Parents.Count > 1
            ? SchemeWeaverConstants.SourceTypes.Ancestor
            : type.SourceType;

    /// <summary>
    /// Editor/range compatibility: date editors only onto date ranges, media only onto
    /// image or URL ranges, URL sources only onto URL or Thing ranges, numeric editors only
    /// onto ranges that take a number or text, everything else (text, tags, dropdowns, the
    /// node name) onto anything.
    /// </summary>
    private bool IsCompatible(NeighbourProperty source, SchemaPropertyInfo target)
    {
        var accepted = target.AcceptedTypes;

        if (IsDateSource(source))
            return accepted.Any(t => t is "DateTime" or "Date" or "DateTimeOffset");

        if (SchemeWeaverConstants.PropertyEditors.MediaPickerAliases.Contains(source.EditorAlias))
            return accepted.Any(t => IsUrl(t) || string.Equals(t, "ImageObject", StringComparison.OrdinalIgnoreCase) || _graph.IsSubtypeOf("ImageObject", t));

        if (IsUrlSource(source))
            return accepted.Any(t => IsUrl(t) || string.Equals(t, "Thing", StringComparison.OrdinalIgnoreCase));

        if (IsNumberSource(source))
            return accepted.Any(t => IsNumber(t) || IsText(t));

        return true;
    }

    private static bool IsDateSource(NeighbourProperty source)
        => string.Equals(source.EditorAlias, DateTimeEditor, StringComparison.OrdinalIgnoreCase)
           || string.Equals(source.Alias, SchemeWeaverConstants.BuiltInProperties.CreateDate, StringComparison.OrdinalIgnoreCase)
           || string.Equals(source.Alias, SchemeWeaverConstants.BuiltInProperties.UpdateDate, StringComparison.OrdinalIgnoreCase);

    private static bool IsUrlSource(NeighbourProperty source)
        => string.Equals(source.Alias, SchemeWeaverConstants.BuiltInProperties.Url, StringComparison.OrdinalIgnoreCase)
           || string.Equals(source.EditorAlias, MultiUrlPickerEditor, StringComparison.OrdinalIgnoreCase);

    private static bool IsNumberSource(NeighbourProperty source)
        => string.Equals(source.EditorAlias, IntegerEditor, StringComparison.OrdinalIgnoreCase)
           || string.Equals(source.EditorAlias, DecimalEditor, StringComparison.OrdinalIgnoreCase);

    private static bool IsUrl(string acceptedType)
        => acceptedType is "Uri" or "URL" or "Url";

    /// <summary>The registry names Schema.org's Integer and Number ranges after the CLR types Schema.NET declares.</summary>
    private static bool IsNumber(string acceptedType)
        => acceptedType is "Integer" or "Number";

    /// <summary>The registry names a text range "String"; "Text" is Schema.org's own name for it.</summary>
    private static bool IsText(string acceptedType)
        => acceptedType is "String" or "Text";

    private static string UniqueKey(Dictionary<string, NeighbourOption> offered, params string[] parts)
    {
        var id = JudgmentSession.Id(parts);
        var candidate = id;
        for (var n = 2; offered.ContainsKey(candidate); n++)
            candidate = $"{id}_{n}";
        return candidate;
    }

    private static int ToPercent(double probability)
        => Math.Clamp((int)Math.Round(probability * 100, MidpointRounding.AwayFromZero), 0, 100);

    /// <summary>One offered option: the neighbour type, its property, and the description the model reads.</summary>
    private sealed record NeighbourOption(NeighbourType Type, NeighbourProperty Property, string Description);
}
