using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services;

/// <summary>
/// The <see cref="ISchemaAutoMapper"/> decorator that puts TypeSafe on the auto-map seam.
/// The request path calls <see cref="SuggestMappingsAsync"/>; when the satellite is
/// configured that goes to <see cref="ITypeSafePropertyMapper"/> with the heuristic's
/// suggestions as priors, and on any failure, or when it is not configured, to whatever
/// <see cref="ISchemaAutoMapper"/> was registered before this one (the <em>prior</em>).
/// </summary>
/// <remarks>
/// <para>
/// Two different mappers are wrapped on purpose. The <em>prior</em> is the fallback: the
/// heuristic on a plain install, or the AI satellite's mapper when both satellites are
/// installed (the composer is ordered after every seam-replacing composer, so this is a
/// fixed cascade: TypeSafe, then AI, then the heuristic inside the AI mapper). The concrete
/// <see cref="SchemaAutoMapper"/> is always the heuristic and is consulted for <em>priors</em>
/// regardless of what the prior is: its rule-driven rows (popular-defaults shapes such as
/// <c>FAQPage.mainEntity</c>, cross-piece references, exact-alias matches) are kept and
/// TypeSafe supplies calibrated confidence for everything else. That merge is a design
/// decision measured after the fact by <c>eval/run-typesafe.mjs</c>, and it lives in the
/// mapper, not here.
/// </para>
/// <para>
/// The catch-all in <see cref="SuggestMappingsAsync"/> is by policy (CLAUDE.md, error
/// handling): an auto-map suggestion is a convenience and a failed API call must degrade to
/// the prior's answer, never to an error in the backoffice modal. The sync
/// <see cref="SuggestMappings"/> and <see cref="RankSchemaProperties"/> paths never involve
/// TypeSafe and delegate straight to the prior.
/// </para>
/// </remarks>
public sealed class TypeSafeSchemaAutoMapper : ISchemaAutoMapper
{
    private readonly ITypeSafePropertyMapper _mapper;
    private readonly ITypeSafeClient _client;
    private readonly ISchemaAutoMapper _prior;
    private readonly SchemaAutoMapper _heuristic;
    private readonly TypeSafeOptions _options;
    private readonly ILogger<TypeSafeSchemaAutoMapper> _logger;

    /// <param name="mapper">The TypeSafe property mapper (the four-round judgment pipeline).</param>
    /// <param name="client">Consulted only for <see cref="ITypeSafeClient.IsConfigured"/>; the mapper owns the calls.</param>
    /// <param name="prior">The <see cref="ISchemaAutoMapper"/> registered before this one; the fallback.</param>
    /// <param name="heuristic">The core name-matching mapper, whose suggestions become priors for the TypeSafe merge.</param>
    /// <param name="options">Satellite options; the model alias is reported in logs.</param>
    /// <param name="logger">Logger for fallback events. The API key is never logged.</param>
    public TypeSafeSchemaAutoMapper(
        ITypeSafePropertyMapper mapper,
        ITypeSafeClient client,
        ISchemaAutoMapper prior,
        SchemaAutoMapper heuristic,
        IOptions<TypeSafeOptions> options,
        ILogger<TypeSafeSchemaAutoMapper> logger)
    {
        _mapper = mapper;
        _client = client;
        _prior = prior;
        _heuristic = heuristic;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>The mapper this one falls back to. Exposed for the startup status line and tests.</summary>
    internal ISchemaAutoMapper Prior => _prior;

    /// <summary>Short type name of <see cref="Prior"/>, e.g. <c>SchemaAutoMapper</c> or <c>AiSchemaAutoMapper</c>.</summary>
    internal string PriorTypeName => _prior.GetType().Name;

    /// <summary>
    /// TypeSafe suggestions when the satellite is configured, with the heuristic's output as
    /// priors; the prior mapper's suggestions otherwise, and on any failure.
    /// </summary>
    public async Task<IEnumerable<PropertyMappingSuggestion>> SuggestMappingsAsync(
        string contentTypeAlias,
        string schemaTypeName)
    {
        if (!_client.IsConfigured)
        {
            return await _prior.SuggestMappingsAsync(contentTypeAlias, schemaTypeName).ConfigureAwait(false);
        }

        try
        {
            var priors = _heuristic.SuggestMappings(contentTypeAlias, schemaTypeName).ToList();
            var suggestions = await _mapper
                .MapAsync(contentTypeAlias, schemaTypeName, priors, CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "SchemeWeaver TypeSafe: {Model} produced {Count} suggestion(s) for {ContentType} as {SchemaType} from {PriorCount} heuristic prior(s).",
                _options.Model,
                suggestions.Count,
                contentTypeAlias,
                schemaTypeName,
                priors.Count);

            return suggestions;
        }
        catch (Exception ex)
        {
            // By policy: never let a mapping-assist failure reach the backoffice as an error.
            _logger.LogWarning(
                ex,
                "SchemeWeaver TypeSafe: mapping {ContentType} as {SchemaType} failed; falling back to {Prior}.",
                contentTypeAlias,
                schemaTypeName,
                PriorTypeName);

            return await _prior.SuggestMappingsAsync(contentTypeAlias, schemaTypeName).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous suggestions never involve TypeSafe: straight to the prior.</summary>
    public IEnumerable<PropertyMappingSuggestion> SuggestMappings(string contentTypeAlias, string schemaTypeName)
        => _prior.SuggestMappings(contentTypeAlias, schemaTypeName);

    /// <summary>Property ranking is the prior's (ultimately the heuristic's) concern.</summary>
    public IEnumerable<RankedSchemaPropertyInfo> RankSchemaProperties(string schemaTypeName)
        => _prior.RankSchemaProperties(schemaTypeName);
}
