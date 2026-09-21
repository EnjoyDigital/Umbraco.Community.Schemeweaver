using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services;

/// <summary>
/// Suggests a Schema.org type for a content type by hierarchical classification: the same
/// beam search the property mapper uses for nested types, started at <c>Thing</c>. Every
/// level is a small Choice over direct subtypes with "stay here" as the default, so the
/// model never sees the whole ~780-type list and cannot name a type the registry lacks.
/// </summary>
public sealed class TypeSafeSchemaTypeSuggester : ITypeSafeSchemaTypeSuggester
{
    private const string Root = "Thing";

    private readonly ITypeSafeClient _client;
    private readonly ISchemaTypeGraph _graph;
    private readonly IContentTypeService _contentTypeService;
    private readonly IServiceProvider _serviceProvider;
    private readonly TypeSafeOptions _options;
    private readonly ILogger<TypeSafeSchemaTypeSuggester> _logger;

    public TypeSafeSchemaTypeSuggester(
        ITypeSafeClient client,
        ISchemaTypeGraph graph,
        IContentTypeService contentTypeService,
        IServiceProvider serviceProvider,
        IOptions<TypeSafeOptions> options,
        ILogger<TypeSafeSchemaTypeSuggester> logger)
    {
        _client = client;
        _graph = graph;
        _contentTypeService = contentTypeService;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TypeSafeSchemaTypeSuggestion>> SuggestAsync(
        string contentTypeAlias,
        int maxResults = 3,
        CancellationToken cancellationToken = default)
    {
        if (maxResults <= 0)
            return [];

        var snapshot = await ContentTypeSnapshot
            .BuildAsync(_contentTypeService, _serviceProvider, contentTypeAlias, _logger, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            _logger.LogDebug("TypeSafe suggester: content type {ContentType} not found", contentTypeAlias);
            return [];
        }

        if (_graph.ChildrenOf(Root).Count == 0)
        {
            _logger.LogDebug("TypeSafe suggester: the schema type graph has no subtypes of {Root}; nothing to classify against", Root);
            return [];
        }

        var session = new JudgmentSession(_client, _options.MaxQuestionsPerRequest, _logger);
        var state = snapshot.ToState(targetSchemaType: null);

        var description = string.IsNullOrWhiteSpace(snapshot.Description) ? string.Empty : $", described as \"{snapshot.Description}\"";
        var subject = new DescentSubject(
            "type",
            Root,
            $"The Umbraco content type `{snapshot.Alias}` (name \"{snapshot.Name}\"{description}) is the document type "
            + "of a page or record whose properties are listed in the state. Classify what a page of this type "
            + "represents as a Schema.org type.");

        // A wider beam than the nested-type descent when more results are wanted, so the
        // alternatives are real alternatives rather than the same path truncated.
        var beamSearch = new BeamSearch(session, _graph, _options.MaxOptionsPerChoice, _logger);
        var beams = await beamSearch
            .DescendAsync(state, [subject], Math.Max(_options.BeamWidth, maxResults), _options.MaxDescentDepth, cancellationToken)
            .ConfigureAwait(false);

        var results = new List<TypeSafeSchemaTypeSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (beams.TryGetValue(subject.Key, out var ranked))
        {
            foreach (var beam in ranked)
            {
                // Distinct landing types: multiple inheritance can reach one type by two paths
                // (LocalBusiness via Organization and via Place); the better path represents it.
                if (!seen.Add(beam.Type))
                    continue;

                results.Add(new TypeSafeSchemaTypeSuggestion(
                    beam.Type,
                    Math.Clamp((int)Math.Round(beam.GeoMean * 100, MidpointRounding.AwayFromZero), 0, 100),
                    beam.Path));

                if (results.Count >= maxResults)
                    break;
            }
        }

        _logger.LogInformation(
            "TypeSafe suggested {Count} schema type(s) for {ContentType} (best: {Best}), {Requests} request(s), {Questions} question(s), {InputTokens} input tokens",
            results.Count, contentTypeAlias, results.Count > 0 ? results[0].SchemaTypeName : "none",
            session.Requests, session.Questions, session.InputTokens);

        return results;
    }
}
