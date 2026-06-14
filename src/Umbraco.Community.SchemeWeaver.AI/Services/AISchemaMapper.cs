using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Umbraco.AI.Core.Chat;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.AI.Models;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.AI.Services;

/// <summary>
/// Uses Umbraco.AI chat completions to suggest Schema.org types and property mappings.
/// Falls back to heuristic mappings when AI is unavailable or returns invalid data.
/// </summary>
public class AISchemaMapper : IAISchemaMapper
{
    private readonly IAIChatService _chatService;
    private readonly IContentTypeService _contentTypeService;
    private readonly ISchemaTypeRegistry _schemaTypeRegistry;
    private readonly ISchemaAutoMapper _heuristicMapper;
    private readonly ILogger<AISchemaMapper> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AISchemaMapper(
        IAIChatService chatService,
        IContentTypeService contentTypeService,
        ISchemaTypeRegistry schemaTypeRegistry,
        ISchemaAutoMapper heuristicMapper,
        ILogger<AISchemaMapper> logger)
    {
        _chatService = chatService;
        _contentTypeService = contentTypeService;
        _schemaTypeRegistry = schemaTypeRegistry;
        _heuristicMapper = heuristicMapper;
        _logger = logger;
    }

    public async Task<SchemaTypeSuggestion[]> SuggestSchemaTypesAsync(
        string contentTypeAlias, CancellationToken ct = default)
    {
        var contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
            return [];

        var properties = contentType.PropertyTypes.Select(p => new
        {
            alias = p.Alias,
            name = p.Name,
            editor = p.PropertyEditorAlias,
            description = p.Description ?? ""
        }).ToArray();

        var propertyLines = string.Join("\n", properties.Select(p =>
            $"  - {p.alias} ({p.editor}) — {(string.IsNullOrEmpty(p.description) ? p.name : p.description)}"));

        var schemaTypeList = string.Join(", ", _schemaTypeRegistry.GetAllTypes().Select(t => t.Name));
        const string exampleFormat = """[{"schemaTypeName": "Article", "confidence": 95, "reasoning": "short explanation"}]""";
        var userPrompt = $"""
            Analyse this Umbraco content type and suggest the most appropriate Schema.org types.

            Content Type: {contentType.Name} (alias: {contentType.Alias})
            Properties:
            {propertyLines}

            Available Schema.org types to choose from:
            {schemaTypeList}

            Return a JSON array of up to 3 suggestions, ranked by confidence. Format:
            {exampleFormat}

            Return ONLY the JSON array, no markdown or explanation.
            """;

        try
        {
            var response = await _chatService.GetChatResponseAsync(
                chat => chat.WithAlias("schemeweaver-schema-type-suggestion"),
                [
                    new ChatMessage(ChatRole.System, SystemPrompts.SchemaTypeSelection),
                    new ChatMessage(ChatRole.User, userPrompt),
                ],
                ct);

            var json = ExtractJson(response.Text ?? "");
            var suggestions = JsonSerializer.Deserialize<SchemaTypeSuggestion[]>(json, JsonOptions);

            if (suggestions is { Length: > 0 })
            {
                // Validate that suggested types actually exist in the registry
                return suggestions
                    .Where(s => _schemaTypeRegistry.GetType(s.SchemaTypeName) is not null)
                    .ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI schema type suggestion failed for {ContentType}, returning empty", contentTypeAlias);
        }

        return [];
    }

    public async Task<BulkSchemaTypeSuggestion[]> SuggestSchemaTypesForAllAsync(
        CancellationToken ct = default)
    {
        var contentTypes = _contentTypeService.GetAll()
            .Where(t => !t.IsElement)
            .ToArray();

        if (contentTypes.Length == 0)
            return [];

        var summaries = contentTypes.Select(t => new
        {
            alias = t.Alias,
            name = t.Name,
            properties = t.PropertyTypes.Select(p => $"{p.Alias} ({p.PropertyEditorAlias})").Take(10)
        });

        var contentTypeLines = string.Join("\n", summaries.Select(s =>
            $"- {s.name} (alias: {s.alias}): properties: {string.Join(", ", s.properties)}"));
        var schemaTypeList = string.Join(", ", _schemaTypeRegistry.GetAllTypes().Select(t => t.Name));
        const string bulkExampleFormat = """[{"contentTypeAlias": "blogPost", "suggestions": [{"schemaTypeName": "BlogPosting", "confidence": 90, "reasoning": "..."}]}]""";
        var userPrompt = $"""
            Analyse these Umbraco content types and suggest the most appropriate Schema.org type for each.

            Content Types:
            {contentTypeLines}

            Available Schema.org types:
            {schemaTypeList}

            Return a JSON array with one entry per content type. Format:
            {bulkExampleFormat}

            Return ONLY the JSON array, no markdown or explanation.
            """;

        try
        {
            var response = await _chatService.GetChatResponseAsync(
                chat => chat.WithAlias("schemeweaver-bulk-schema-suggestion"),
                [
                    new ChatMessage(ChatRole.System, SystemPrompts.SchemaTypeSelection),
                    new ChatMessage(ChatRole.User, userPrompt),
                ],
                ct);

            var json = ExtractJson(response.Text ?? "");
            var results = JsonSerializer.Deserialize<BulkSchemaTypeSuggestion[]>(json, JsonOptions);

            if (results is { Length: > 0 })
            {
                // Enrich with content type display names and validate schema types
                foreach (var result in results)
                {
                    var ct2 = contentTypes.FirstOrDefault(c =>
                        c.Alias.Equals(result.ContentTypeAlias, StringComparison.OrdinalIgnoreCase));
                    result.ContentTypeName = ct2?.Name;
                    result.Suggestions = result.Suggestions
                        .Where(s => _schemaTypeRegistry.GetType(s.SchemaTypeName) is not null)
                        .ToArray();
                }

                return results.Where(r => r.Suggestions.Length > 0).ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI bulk schema type suggestion failed");
        }

        return [];
    }

    public async Task<PropertyMappingSuggestion[]> SuggestPropertyMappingsAsync(
        string contentTypeAlias, string schemaTypeName, CancellationToken ct = default)
    {
        // Always get heuristic suggestions as the baseline/fallback
        var heuristicSuggestions = _heuristicMapper.SuggestMappings(contentTypeAlias, schemaTypeName).ToArray();

        var contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
            return heuristicSuggestions;

        var contentProperties = contentType.PropertyTypes.Select(p => new
        {
            alias = p.Alias,
            name = p.Name,
            editor = p.PropertyEditorAlias,
            description = p.Description ?? ""
        }).ToArray();

        var schemaProperties = _schemaTypeRegistry.GetProperties(schemaTypeName).ToArray();

        var contentPropertyLines = string.Join("\n", contentProperties.Select(p =>
            $"  - {p.alias} ({p.editor}) — {(string.IsNullOrEmpty(p.description) ? p.name : p.description)}"));
        var schemaPropertyLines = string.Join("\n", schemaProperties.Select(p =>
            $"  - {p.Name} ({p.PropertyType}){(p.IsComplexType ? " [complex type]" : "")}"));
        const string mappingExampleFormat = """[{"schemaPropertyName": "headline", "suggestedContentTypePropertyAlias": "title", "suggestedSourceType": "property", "confidence": 90}]""";
        var userPrompt = $"""
            Map these Umbraco content type properties to Schema.org {schemaTypeName} properties.

            Content Type: {contentType.Name} (alias: {contentType.Alias})
            Properties:
            {contentPropertyLines}

            Built-in properties always available:
              - __name (content node name)
              - __url (content URL)
              - __createDate (creation date)
              - __updateDate (last modified date)

            Schema.org Type: {schemaTypeName}
            Schema Properties:
            {schemaPropertyLines}

            For each schema property where you can find a matching content property, return a suggestion.
            Only suggest mappings where there is a meaningful semantic match.

            Source types:
            - "property" — map from a content type property on the current node
            - "static" — hardcoded value (set suggestedContentTypePropertyAlias to null, use staticValue field)

            For built-in properties (__name, __url, __createDate, __updateDate), use source type "property".

            Return a JSON array. Format:
            {mappingExampleFormat}

            Return ONLY the JSON array, no markdown or explanation.
            """;

        try
        {
            var response = await _chatService.GetChatResponseAsync(
                chat => chat.WithAlias("schemeweaver-property-mapping"),
                [
                    new ChatMessage(ChatRole.System, SystemPrompts.PropertyMapping),
                    new ChatMessage(ChatRole.User, userPrompt),
                ],
                ct);

            var json = ExtractJson(response.Text ?? "");
            var aiSuggestions = JsonSerializer.Deserialize<PropertyMappingSuggestion[]>(json, JsonOptions);

            if (aiSuggestions is { Length: > 0 })
            {
                return MergeSuggestions(heuristicSuggestions, aiSuggestions, schemaProperties);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI property mapping failed for {ContentType}/{SchemaType}, using heuristic fallback",
                contentTypeAlias, schemaTypeName);
        }

        return heuristicSuggestions;
    }

    /// <summary>
    /// Merges AI suggestions with heuristic suggestions. For each schema property,
    /// uses the suggestion with the higher confidence score.
    /// </summary>
    private static PropertyMappingSuggestion[] MergeSuggestions(
        PropertyMappingSuggestion[] heuristic,
        PropertyMappingSuggestion[] ai,
        SchemaPropertyInfo[] schemaProperties)
    {
        var merged = new Dictionary<string, PropertyMappingSuggestion>(StringComparer.OrdinalIgnoreCase);

        // Start with heuristic suggestions
        foreach (var h in heuristic)
        {
            merged[h.SchemaPropertyName] = h;
        }

        // Overlay AI suggestions where they have higher confidence
        foreach (var a in ai)
        {
            if (string.IsNullOrEmpty(a.SchemaPropertyName))
                continue;

            if (!merged.TryGetValue(a.SchemaPropertyName, out var existing) || a.Confidence > existing.Confidence)
            {
                // Enrich AI suggestion with schema property metadata from the registry
                var schemaProp = schemaProperties.FirstOrDefault(p =>
                    p.Name.Equals(a.SchemaPropertyName, StringComparison.OrdinalIgnoreCase));

                if (schemaProp is not null)
                {
                    a.SchemaPropertyType = schemaProp.PropertyType;
                    a.AcceptedTypes = schemaProp.AcceptedTypes;
                    a.IsComplexType = schemaProp.IsComplexType;
                }

                a.IsAutoMapped = true;
                merged[a.SchemaPropertyName] = a;
            }
        }

        // Ensure all schema properties are represented (even unmapped ones)
        foreach (var prop in schemaProperties)
        {
            if (!merged.ContainsKey(prop.Name))
            {
                merged[prop.Name] = new PropertyMappingSuggestion
                {
                    SchemaPropertyName = prop.Name,
                    SchemaPropertyType = prop.PropertyType,
                    AcceptedTypes = prop.AcceptedTypes,
                    IsComplexType = prop.IsComplexType,
                    Confidence = 0,
                    IsAutoMapped = false,
                };
            }
        }

        return merged.Values
            .OrderByDescending(s => s.Confidence)
            .ThenBy(s => s.SchemaPropertyName)
            .ToArray();
    }

    /// <summary>
    /// Extracts a JSON array from a response that may contain markdown fences or extra text.
    /// </summary>
    public static string ExtractJson(string text)
    {
        text = text.Trim();

        // Strip markdown code fences
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
                text = text[(firstNewline + 1)..];

            var lastFence = text.LastIndexOf("```");
            if (lastFence > 0)
                text = text[..lastFence];

            text = text.Trim();
        }

        // Find the JSON array boundaries
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');

        if (start >= 0 && end > start)
            return text[start..(end + 1)];

        return text;
    }
}
