using Microsoft.Extensions.Logging;
using Umbraco.Community.SchemeWeaver.AI.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// An implementation of <see cref="ISchemaAutoMapper"/> that delegates to the AI mapper
/// (<see cref="IAISchemaMapper"/>) for the async path and falls back transparently to the
/// heuristic <see cref="SchemaAutoMapper"/> on failure or when the sync path is used.
/// </summary>
/// <remarks>
/// Registered by <c>SchemeWeaverAIComposer</c> after the main package composer, so it
/// replaces the heuristic registration for the <see cref="ISchemaAutoMapper"/> interface
/// while the concrete <see cref="SchemaAutoMapper"/> remains resolvable as the fallback
/// dependency.
/// </remarks>
public sealed class AiSchemaAutoMapper : ISchemaAutoMapper
{
    private readonly IAISchemaMapper _aiMapper;
    private readonly SchemaAutoMapper _heuristic;
    private readonly ILogger<AiSchemaAutoMapper> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="AiSchemaAutoMapper"/>.
    /// </summary>
    /// <param name="aiMapper">The AI-backed mapper used for async property-mapping suggestions.</param>
    /// <param name="heuristic">The heuristic mapper used as the sync fallback and for property ranking.</param>
    /// <param name="logger">Logger for recording fallback events.</param>
    public AiSchemaAutoMapper(
        IAISchemaMapper aiMapper,
        SchemaAutoMapper heuristic,
        ILogger<AiSchemaAutoMapper> logger)
    {
        _aiMapper = aiMapper;
        _heuristic = heuristic;
        _logger = logger;
    }

    /// <summary>
    /// Suggests property mappings via the AI mapper. Falls back to the heuristic mapper
    /// if the AI call throws an unhandled exception. The AI mapper already catches most
    /// AI failures internally and falls back itself, so this outer catch is a last resort.
    /// </summary>
    public async Task<IEnumerable<PropertyMappingSuggestion>> SuggestMappingsAsync(
        string contentTypeAlias,
        string schemaTypeName)
    {
        try
        {
            return await _aiMapper.SuggestPropertyMappingsAsync(contentTypeAlias, schemaTypeName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "AI property mapping raised an unhandled exception for {ContentType}/{SchemaType}; falling back to heuristic.",
                contentTypeAlias,
                schemaTypeName);

            return _heuristic.SuggestMappings(contentTypeAlias, schemaTypeName);
        }
    }

    /// <summary>
    /// Synchronous property-mapping suggestions — delegates directly to the heuristic mapper.
    /// The async path (<see cref="SuggestMappingsAsync"/>) is the preferred entry point;
    /// this overload exists for internal callers that cannot await.
    /// </summary>
    public IEnumerable<PropertyMappingSuggestion> SuggestMappings(
        string contentTypeAlias,
        string schemaTypeName)
        => _heuristic.SuggestMappings(contentTypeAlias, schemaTypeName);

    /// <summary>
    /// Returns the Schema.org properties for <paramref name="schemaTypeName"/> ranked by
    /// usefulness as a nested-type mapping target. Delegates to the heuristic mapper.
    /// </summary>
    public IEnumerable<RankedSchemaPropertyInfo> RankSchemaProperties(string schemaTypeName)
        => _heuristic.RankSchemaProperties(schemaTypeName);
}
