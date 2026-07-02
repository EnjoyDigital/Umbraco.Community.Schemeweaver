using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.AI.Core.Chat;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.AI.Models;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;

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
    private readonly SchemaAutoMapper _heuristicMapper;
    private readonly IPropertyValueSchemaService _valueSchemaService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AISchemaMapper> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Initialises a new instance of <see cref="AISchemaMapper"/>.
    /// </summary>
    /// <remarks>
    /// The <paramref name="heuristicMapper"/> parameter takes the concrete
    /// <see cref="SchemaAutoMapper"/> (not <see cref="ISchemaAutoMapper"/>) to avoid
    /// a circular dependency: the <c>ISchemaAutoMapper</c> registration in the AI
    /// composer resolves to <see cref="AiSchemaAutoMapper"/>, which depends on
    /// <see cref="IAISchemaMapper"/>, which would circle back here.
    /// For the SAME reason, <see cref="ISchemeWeaverService"/> is NOT injected directly
    /// (it depends on <c>ISchemaAutoMapper</c>, closing the loop and dead-locking DI
    /// resolution) — it is resolved lazily from <paramref name="serviceProvider"/> at
    /// call time, by which point this scoped instance is already cached in the scope.
    /// </remarks>
    public AISchemaMapper(
        IAIChatService chatService,
        IContentTypeService contentTypeService,
        ISchemaTypeRegistry schemaTypeRegistry,
        SchemaAutoMapper heuristicMapper,
        IPropertyValueSchemaService valueSchemaService,
        IServiceProvider serviceProvider,
        ILogger<AISchemaMapper> logger)
    {
        _chatService = chatService;
        _contentTypeService = contentTypeService;
        _schemaTypeRegistry = schemaTypeRegistry;
        _heuristicMapper = heuristicMapper;
        _valueSchemaService = valueSchemaService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Renders one prompt line per property: <c>alias (editor) — description</c>, plus the Umbraco
    /// 17.4+ value JSON Schema (the actual stored-value shape) on a continuation line when available,
    /// so the model reasons about real value structure — types, lengths, UUID/crop/range — not just
    /// the editor alias. Degrades to the alias-only line when no schema is available.
    /// </summary>
    /// <summary>Per-property value-schema cap (chars) so a recursive Block List schema can't balloon the prompt.</summary>
    private const int MaxValueSchemaChars = 600;

    /// <summary>Block editors whose element-type structure we expand into the prompt.</summary>
    private static readonly HashSet<string> BlockEditorAliases =
        new(StringComparer.OrdinalIgnoreCase) { "Umbraco.BlockList", "Umbraco.BlockGrid" };

    private async Task<string> BuildPropertyLinesAsync(
        IEnumerable<IPropertyType> propertyTypes, string? contentTypeAlias = null)
    {
        // Resolved lazily (and only when a block property is encountered) to break the DI
        // cycle described on the constructor — see remarks there.
        ISchemeWeaverService? schemeWeaverService = null;

        var lines = new List<string>();
        foreach (var p in propertyTypes)
        {
            var description = string.IsNullOrEmpty(p.Description) ? p.Name : p.Description;
            var valueSchema = await _valueSchemaService.GetDataTypeValueSchemaAsync(p.DataTypeKey).ConfigureAwait(false);
            var schemaHint = string.IsNullOrEmpty(valueSchema)
                ? string.Empty
                : $"\n      value schema: {Truncate(valueSchema, MaxValueSchemaChars)}";

            // For Block List/Grid properties, expand the allowed element types and their inner
            // properties so the model can choose the right blockContent shape (stringList vs
            // nestedMappings vs routes) and reference real inner aliases.
            var blockHint = string.Empty;
            if (contentTypeAlias is not null && BlockEditorAliases.Contains(p.PropertyEditorAlias))
            {
                schemeWeaverService ??= _serviceProvider.GetRequiredService<ISchemeWeaverService>();
                var elementTypes = await schemeWeaverService
                    .GetBlockElementTypesAsync(contentTypeAlias, p.Alias).ConfigureAwait(false);
                var rendered = RenderBlockElementTypes(elementTypes, depth: 0);
                if (!string.IsNullOrEmpty(rendered))
                    blockHint = $"\n      block element types:\n{rendered}";
            }

            lines.Add($"  - {p.Alias} ({p.PropertyEditorAlias}) — {description}{schemaHint}{blockHint}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>Max block-nesting depth rendered into the prompt — enough to reveal a nested Block
    /// List (so the model knows to use <c>routes</c>) without ballooning the token budget.</summary>
    private const int MaxBlockRenderDepth = 2;

    /// <summary>
    /// Renders block element types as indented lines: each element type's alias/name and its inner
    /// properties (alias + editor), recursing one level into any property that is itself a Block
    /// List/Grid so nested blocks are visible to the model.
    /// </summary>
    private static string RenderBlockElementTypes(IEnumerable<BlockElementTypeInfo> elementTypes, int depth)
    {
        var indent = new string(' ', 8 + depth * 4);
        var lines = new List<string>();
        foreach (var et in elementTypes)
        {
            var inner = string.Join(", ", et.PropertyInfos.Select(ip => $"{ip.Alias} ({ip.EditorAlias})"));
            lines.Add($"{indent}· block '{et.Alias}' ({et.Name}): {inner}");

            if (depth < MaxBlockRenderDepth)
            {
                foreach (var ip in et.PropertyInfos.Where(ip => ip.NestedBlockElementTypes.Count > 0))
                {
                    lines.Add($"{indent}    nested block list '{ip.Alias}' contains:");
                    lines.Add(RenderBlockElementTypes(ip.NestedBlockElementTypes, depth + 1));
                }
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>Caps a value schema for the prompt — a large recursive schema is truncated with a marker
    /// rather than blowing the token budget; the head (type/constraints) carries the most mapping signal.</summary>
    private static string Truncate(string value, int maxChars)
        => value.Length <= maxChars ? value : value[..maxChars] + " …(schema truncated)";

    /// <summary>Number of ranked schema properties surfaced to the model (most-important first).</summary>
    private const int MaxSchemaPropertiesRendered = 45;

    /// <summary>
    /// Renders the Schema.org properties ranked by importance, tagging REQUIRED / popular / complex
    /// and listing accepted types, so the model maps the rich-result-critical properties first.
    /// </summary>
    private static string RenderSchemaProperties(IReadOnlyList<RankedSchemaPropertyInfo> ranked)
    {
        var shown = ranked.Take(MaxSchemaPropertiesRendered).Select(p =>
        {
            var tags = new List<string>();
            if (p.IsRequired) tags.Add("REQUIRED");
            if (p.IsPopular) tags.Add("popular");
            if (p.IsComplexType) tags.Add("complex");
            var accepts = p.AcceptedTypes is { Count: > 0 }
                ? $" accepts:[{string.Join(", ", p.AcceptedTypes)}]"
                : string.Empty;
            var tagPart = tags.Count > 0 ? $" [{string.Join(",", tags)}]" : string.Empty;
            return $"  - {p.Name} ({p.PropertyType}){accepts} rank:{p.Confidence}{tagPart}";
        });

        var lines = string.Join("\n", shown);
        if (ranked.Count > MaxSchemaPropertiesRendered)
            lines += $"\n  …and {ranked.Count - MaxSchemaPropertiesRendered} more lower-ranked properties.";
        return lines;
    }

    /// <summary>Renders the heuristic's name-only mappings as a baseline the model is told to improve on.</summary>
    private static string RenderHeuristicBaseline(IEnumerable<PropertyMappingSuggestion> heuristic)
    {
        var rows = heuristic
            .Where(h => h.IsAutoMapped && !string.IsNullOrEmpty(h.SuggestedContentTypePropertyAlias))
            .Select(h => $"  - {h.SchemaPropertyName} <- {h.SuggestedSourceType}:{h.SuggestedContentTypePropertyAlias} @{h.Confidence}");
        var joined = string.Join("\n", rows);
        return string.IsNullOrEmpty(joined) ? "  (none)" : joined;
    }

    public async Task<SchemaTypeSuggestion[]> SuggestSchemaTypesAsync(
        string contentTypeAlias, CancellationToken ct = default)
    {
        var contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
            return [];

        var propertyLines = await BuildPropertyLinesAsync(contentType.PropertyTypes).ConfigureAwait(false);

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

            var suggestions = DeserializeArrayResilient<SchemaTypeSuggestion>(response.Text ?? "");

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

            var results = DeserializeArrayResilient<BulkSchemaTypeSuggestion>(response.Text ?? "");

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

        // Ranked schema properties: surface importance (popular/required) + accepted types so the
        // model maps the rich-result-critical properties first. Also reused to enrich/merge below.
        var schemaProperties = _heuristicMapper.RankSchemaProperties(schemaTypeName)
            .OrderByDescending(p => p.Confidence)
            .ToArray();

        var contentPropertyLines = await BuildPropertyLinesAsync(contentType.PropertyTypes, contentType.Alias)
            .ConfigureAwait(false);
        var schemaPropertyLines = RenderSchemaProperties(schemaProperties);
        var heuristicBaselineLines = RenderHeuristicBaseline(heuristicSuggestions);

        var userPrompt = $"""
            Content type: {contentType.Name} (alias: {contentType.Alias})
            Target Schema.org type: {schemaTypeName}

            CONTENT PROPERTIES:
            {contentPropertyLines}

            Built-in properties (source type "property"): __name, __url, __createDate, __updateDate

            SCHEMA.ORG {schemaTypeName} PROPERTIES (ranked; map the important ones first):
            {schemaPropertyLines}

            HEURISTIC BASELINE (name-only; improve on it):
            {heuristicBaselineLines}

            Produce the best mapping as the JSON array described in the system prompt.
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

            // Resilient parse: salvage complete suggestions even if the reply was truncated, so a
            // cut-off response still improves on the heuristic instead of silently discarding it.
            var aiSuggestions = DeserializeArrayResilient<PropertyMappingSuggestion>(response.Text ?? "");

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
    /// Combines AI and heuristic suggestions. The AI is now AUTHORITATIVE: a well-formed AI
    /// suggestion wins outright (it reasons about meaning, source type, nested objects and block
    /// structure — things the name-only heuristic cannot express). The heuristic only fills schema
    /// properties the AI did not map, and every remaining schema property is listed as an unmapped
    /// placeholder for completeness. (Previously the heuristic's high name-match confidence could
    /// suppress the AI, which is exactly why the AI "never improved" on the heuristic.)
    /// </summary>
    private static PropertyMappingSuggestion[] MergeSuggestions(
        PropertyMappingSuggestion[] heuristic,
        PropertyMappingSuggestion[] ai,
        SchemaPropertyInfo[] schemaProperties)
    {
        var merged = new Dictionary<string, PropertyMappingSuggestion>(StringComparer.OrdinalIgnoreCase);

        // 1. AI suggestions are authoritative — take them first.
        foreach (var a in ai)
        {
            if (string.IsNullOrEmpty(a.SchemaPropertyName) || merged.ContainsKey(a.SchemaPropertyName))
                continue;

            // Enrich with schema metadata so the row carries accepted/complex-type info downstream.
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

        // 2. Heuristic fills only the gaps the AI left (real name matches the AI may have skipped).
        foreach (var h in heuristic)
        {
            if (string.IsNullOrEmpty(h.SchemaPropertyName))
                continue;
            if (h.IsAutoMapped && !merged.ContainsKey(h.SchemaPropertyName))
                merged[h.SchemaPropertyName] = h;
        }

        // 3. Completeness: list every remaining schema property as an unmapped placeholder.
        foreach (var prop in schemaProperties.Where(p => !merged.ContainsKey(p.Name)))
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

        return merged.Values
            .OrderByDescending(s => s.Confidence)
            .ThenBy(s => s.SchemaPropertyName)
            .ToArray();
    }

    /// <summary>Strips surrounding markdown code fences (```json … ```), if present.</summary>
    private static string StripCodeFences(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("```"))
            return text;

        var firstNewline = text.IndexOf('\n');
        if (firstNewline > 0)
            text = text[(firstNewline + 1)..];

        var lastFence = text.LastIndexOf("```");
        if (lastFence > 0)
            text = text[..lastFence];

        return text.Trim();
    }

    /// <summary>
    /// Extracts a JSON array from a response that may contain markdown fences or extra text.
    /// </summary>
    public static string ExtractJson(string text)
    {
        text = StripCodeFences(text);

        // Find the JSON array boundaries
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');

        if (start >= 0 && end > start)
            return text[start..(end + 1)];

        return text;
    }

    /// <summary>
    /// Salvages a valid JSON array containing every COMPLETE top-level object from a response,
    /// even when the array was cut off mid-element (e.g. the model hit its output-token limit).
    /// This is the resilient fallback: a truncated AI reply still yields its leading suggestions
    /// instead of being discarded wholesale to the heuristic. A trailing incomplete object is
    /// dropped; if nothing complete is found an empty array <c>[]</c> is returned.
    /// </summary>
    public static string RepairJsonArray(string text)
    {
        // Strip fences only — NOT ExtractJson, whose lastIndexOf(']') would truncate at a ']'
        // that lives inside a (string) resolverConfig value when the array itself is cut off.
        text = StripCodeFences(text);
        var start = text.IndexOf('[');
        if (start < 0)
            return "[]";

        var objects = new List<string>();
        int depth = 0, objStart = -1;
        bool inString = false, escape = false;

        for (var i = start + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (escape) { escape = false; continue; }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            switch (c)
            {
                case '{':
                    if (depth == 0) objStart = i;
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        objects.Add(text[objStart..(i + 1)]);
                        objStart = -1;
                    }
                    break;
                case ']' when depth == 0:
                    i = text.Length; // top-level array closed — stop
                    break;
            }
        }

        return "[" + string.Join(",", objects) + "]";
    }

    /// <summary>
    /// Deserialises a JSON array of <typeparamref name="T"/> from a model response, tolerating
    /// truncation: tries the clean array first, then salvages the complete objects via
    /// <see cref="RepairJsonArray"/>. Returns an empty array (never throws) so callers can fall
    /// back to the heuristic only when genuinely nothing usable came back.
    /// </summary>
    public static T[] DeserializeArrayResilient<T>(string text)
    {
        try
        {
            var clean = JsonSerializer.Deserialize<T[]>(ExtractJson(text), JsonOptions);
            if (clean is { Length: > 0 })
                return clean;
        }
        catch (JsonException)
        {
            // truncated or malformed — fall through to salvage
        }

        try
        {
            return JsonSerializer.Deserialize<T[]>(RepairJsonArray(text), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
