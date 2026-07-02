using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services.Advisory;

namespace Umbraco.Community.SchemeWeaver.Services.Mapping;

/// <summary>
/// A deterministic, range-aware structural enrichment pass that runs AFTER the flat
/// matching loop in <see cref="SchemaAutoMapper"/>. Where the flat loop only name-matches
/// scalars, this dispatcher derives correct rich structures generically from the schema
/// registry and the matched content properties:
/// <list type="number">
///   <item><b>complexType-from-scalar</b> — fills the inner <c>complexTypeMappings</c> of a
///   complex schema property (e.g. <c>Author</c> → <c>Person</c>) that name-matched a scalar
///   content property, by prefix-grouping sibling content properties
///   (<c>locationName</c>/<c>locationAddress</c> → <c>Place.Name</c>/<c>Place.Address</c>).
///   This fixes the dead bare-complexType rows the flat loop emits with a null config.</item>
///   <item><b>blockContent nested objects</b> — supplements a block-backed nested mapping with
///   any extra bindings discoverable by name on the block element type's own properties.</item>
///   <item><b>blockContent stringList</b> — when a single-text-field block element type feeds an
///   array-of-text schema property, emits <c>extractAs:"stringList"</c> with the detected inner
///   property (generalising the hard-coded recipe/howto presets).</item>
///   <item><b>range-validation repair</b> — a final pass that re-points a rich suggestion's nested
///   type onto the target property's accepted range when the heuristic produced an out-of-range
///   type, so nothing is silently swallowed by Schema.NET's typed <c>OneOrMany&lt;T&gt;</c>.</item>
/// </list>
/// Priors (<c>PopularSchemaDefaults</c>, the synonym dictionary) still win where present; this
/// pass only adds structure the flat loop left missing. Confidence thresholds and pre-tick
/// semantics are unchanged — structural rows inherit the matched scalar/block's confidence
/// (floored to the show threshold for a structurally-confirmed string list so a weak partial
/// name match is not dropped after we have proven the shape).
/// </summary>
public sealed class StructuralEnricher
{
    private readonly ISchemaTypeRegistry _registry;
    private readonly Func<string, IReadOnlyList<string>, string?> _matchAlias;
    private readonly int _showThreshold;
    private readonly ILogger? _logger;

    private static readonly HashSet<string> BlockEditorAliases =
        SchemeWeaverConstants.PropertyEditors.BlockEditorAliases;

    /// <param name="registry">Schema registry, for nested-type property lookup and range checks.</param>
    /// <param name="matchAlias">Best-effort exact/synonym/partial match of a schema property name against a set of candidate aliases (mirrors the flat loop), or null when nothing matches.</param>
    /// <param name="showThreshold">The show-confidence threshold, used as a floor for structurally-confirmed rows.</param>
    /// <param name="logger">Optional logger for swallowed per-suggestion enrichment failures (this class is hand-constructed, so the caller's own logger is fine).</param>
    public StructuralEnricher(
        ISchemaTypeRegistry registry,
        Func<string, IReadOnlyList<string>, string?> matchAlias,
        int showThreshold,
        ILogger? logger = null)
    {
        _registry = registry;
        _matchAlias = matchAlias;
        _showThreshold = showThreshold;
        _logger = logger;
    }

    /// <summary>
    /// Enriches <paramref name="suggestions"/> in place. <paramref name="contentPropertyAliases"/>
    /// is the full set of content-type property aliases (for prefix-grouping). <paramref name="blockElementsFor"/>
    /// resolves the block element types behind a block-list content property alias, or returns empty
    /// when block introspection is unavailable (null host service) — in which case the block branches
    /// are inert and the scalar complexType branch still runs.
    /// </summary>
    public void Enrich(
        List<PropertyMappingSuggestion> suggestions,
        IReadOnlyList<string> contentPropertyAliases,
        Func<string, IReadOnlyList<BlockElementTypeInfo>> blockElementsFor)
    {
        foreach (var suggestion in suggestions)
        {
            try
            {
                EnrichOne(suggestion, contentPropertyAliases, blockElementsFor);
            }
            catch (Exception ex)
            {
                // Structural enrichment is strictly additive — never let a malformed block
                // configuration or unexpected registry shape break the flat suggestion.
                _logger?.LogDebug(ex,
                    "Structural enrichment failed for schema property {SchemaProperty} — keeping the flat suggestion",
                    suggestion.SchemaPropertyName);
            }
        }

        RepairRanges(suggestions);
    }

    private void EnrichOne(
        PropertyMappingSuggestion suggestion,
        IReadOnlyList<string> contentPropertyAliases,
        Func<string, IReadOnlyList<BlockElementTypeInfo>> blockElementsFor)
    {
        var matchedAlias = suggestion.SuggestedContentTypePropertyAlias;
        var isBlockMatch = !string.IsNullOrEmpty(suggestion.EditorAlias)
                           && BlockEditorAliases.Contains(suggestion.EditorAlias!);

        // Branch 3b: a structurally-confirmed string list (e.g. from a popular default applied
        // through a weak partial name match) that would otherwise be dropped — floor its
        // confidence to the show threshold so it surfaces.
        if (IsStringList(suggestion.SuggestedResolverConfig))
        {
            suggestion.Confidence = Math.Max(suggestion.Confidence, _showThreshold);
            return;
        }

        if (isBlockMatch && !string.IsNullOrEmpty(matchedAlias))
        {
            var elements = blockElementsFor(matchedAlias!);

            // Branch 3a: array-of-text schema property fed by a single-text-field block element
            // type → string list. Detected from the block, not hard-coded aliases.
            if (AcceptsText(suggestion.AcceptedTypes))
            {
                var innerAlias = DetectSingleTextField(elements);
                if (innerAlias is not null)
                {
                    suggestion.SuggestedSourceType = "blockContent";
                    suggestion.SuggestedNestedSchemaTypeName = null;
                    suggestion.SuggestedResolverConfig = SerialiseStringList(innerAlias);
                    suggestion.Confidence = Math.Max(suggestion.Confidence, _showThreshold);
                    return;
                }
            }

            // Branch 2: block-backed nested object → supplement its nestedMappings with any extra
            // bindings discoverable by name on the block element type's own properties.
            if (string.Equals(suggestion.SuggestedSourceType, "blockContent", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(suggestion.SuggestedNestedSchemaTypeName))
            {
                SupplementNestedMappings(suggestion, elements);
                return;
            }
        }

        // Branch 1: complexType-from-scalar. A complex schema property that name-matched a scalar
        // content property but carries no inner config is dead at runtime — fill its
        // complexTypeMappings by prefix-grouping sibling content properties.
        if (string.Equals(suggestion.SuggestedSourceType, "complexType", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(suggestion.SuggestedNestedSchemaTypeName)
            && !string.IsNullOrEmpty(matchedAlias)
            && !isBlockMatch
            && !HasComplexTypeMappings(suggestion.SuggestedResolverConfig))
        {
            var bindings = BuildComplexTypeBindings(
                suggestion.SuggestedNestedSchemaTypeName!, matchedAlias!, contentPropertyAliases);
            if (bindings.Count > 0)
                suggestion.SuggestedResolverConfig = SerialiseComplexType(bindings);
        }
    }

    // ---- Branch 1: complexType-from-scalar -------------------------------------------------

    /// <summary>
    /// Builds the inner <c>complexTypeMappings</c> for a complex schema property by grouping the
    /// content properties that share a camelCase prefix with the matched scalar. e.g. a match on
    /// <c>locationName</c> groups <c>locationName</c>+<c>locationAddress</c>, binding each one's
    /// suffix (<c>Name</c>, <c>Address</c>) to the corresponding property on the nested type.
    /// Always yields at least a <c>Name</c> binding for the matched scalar so the config is never dead.
    /// </summary>
    private List<ComplexBinding> BuildComplexTypeBindings(
        string nestedType, string matchedAlias, IReadOnlyList<string> contentPropertyAliases)
    {
        var nestedProps = _registry.GetProperties(nestedType).Select(p => p.Name).ToList();
        var bindings = new List<ComplexBinding>();
        var boundContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var boundSchema = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var prefix = CamelPrefix(matchedAlias);

        // Sibling content properties sharing the prefix (incl. the matched one itself).
        var group = contentPropertyAliases
            .Where(a => string.Equals(a, matchedAlias, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(CamelPrefix(a), prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var alias in group)
        {
            var suffix = CamelSuffix(alias);
            var nestedMatch = _matchAlias(suffix, nestedProps);
            if (nestedMatch is null || boundSchema.Contains(nestedMatch) || !boundContent.Add(alias))
                continue;

            boundSchema.Add(nestedMatch);
            bindings.Add(new ComplexBinding(nestedMatch, alias));
        }

        // Guarantee the matched scalar lands on the nested type's Name (the common case) so the
        // resolver config always resolves at least one value.
        if (bindings.Count == 0)
        {
            var nameProp = nestedProps.FirstOrDefault(
                p => string.Equals(p, "Name", StringComparison.OrdinalIgnoreCase)) ?? "Name";
            bindings.Add(new ComplexBinding(nameProp, matchedAlias));
        }

        return bindings;
    }

    // ---- Branch 2: blockContent nested objects --------------------------------------------

    /// <summary>
    /// Supplements a block-backed nested mapping (route or legacy nestedMappings shape) with extra
    /// bindings: for each property the nested Schema.org type exposes that the existing config does
    /// not already bind, name/synonym-match it against the block element type's own properties and
    /// append it. Never removes or rewrites existing bindings (additive only).
    /// </summary>
    private void SupplementNestedMappings(PropertyMappingSuggestion suggestion, IReadOnlyList<BlockElementTypeInfo> elements)
    {
        if (elements.Count == 0)
            return;

        using var doc = TryParse(suggestion.SuggestedResolverConfig);
        // Only the legacy { "nestedMappings": [...] } shape is supplemented here — the routed shape
        // is owned by BlockSchemaSuggester. Bail if the config is a routes object.
        if (doc is not null && doc.RootElement.TryGetProperty("routes", out _))
            return;

        var nestedType = suggestion.SuggestedNestedSchemaTypeName!;
        var nestedProps = _registry.GetProperties(nestedType).ToList();
        if (nestedProps.Count == 0)
            return;

        // Existing bindings (schemaProperty already covered) + parse current list.
        var existing = ReadNestedMappings(doc);
        var covered = new HashSet<string>(existing.Select(b => b.SchemaProperty), StringComparer.OrdinalIgnoreCase);

        // Pool of the element types' own property aliases (flattened across element types).
        var blockAliases = elements
            .SelectMany(e => e.PropertyInfos.Count > 0
                ? e.PropertyInfos.Select(p => p.Alias)
                : e.Properties)
            .Where(a => !string.IsNullOrEmpty(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (blockAliases.Count == 0)
            return;

        var usedContent = new HashSet<string>(existing.Select(b => b.ContentProperty), StringComparer.OrdinalIgnoreCase);
        var added = false;

        foreach (var schemaProp in nestedProps)
        {
            if (covered.Contains(schemaProp.Name))
                continue;

            var available = blockAliases.Where(a => !usedContent.Contains(a)).ToList();
            var match = _matchAlias(schemaProp.Name, available);
            if (match is null)
                continue;

            existing.Add(new NestedBinding(schemaProp.Name, match, null, null));
            covered.Add(schemaProp.Name);
            usedContent.Add(match);
            added = true;
        }

        if (added)
            suggestion.SuggestedResolverConfig = SerialiseNestedMappings(existing);
    }

    // ---- Branch 4: range repair ------------------------------------------------------------

    /// <summary>
    /// Final pass: for every rich suggestion, ensure the nested/complex type it points at is within
    /// the target schema property's accepted range. When the heuristic produced an out-of-range type
    /// (and the property does accept some structured type), re-point it onto the first accepted complex
    /// type so Schema.NET's strongly-typed setter does not silently discard the value.
    /// </summary>
    private void RepairRanges(List<PropertyMappingSuggestion> suggestions)
    {
        foreach (var suggestion in suggestions)
        {
            var nested = suggestion.SuggestedNestedSchemaTypeName;
            if (string.IsNullOrEmpty(nested))
                continue;
            if (!IsRichSourceType(suggestion.SuggestedSourceType))
                continue;

            var accepted = suggestion.AcceptedTypes;
            if (accepted.Count == 0)
                continue;

            // In range already (exact OR a subtype of an accepted type) — nothing to do. The range
            // is checked through the registry's parent chain because Schema.NET often narrows a
            // property to a base type (e.g. recipeInstructions → CreativeWork) while the heuristic
            // legitimately targets a concrete subtype (HowToStep), which IS assignable.
            if (IsWithinRange(nested!, accepted))
                continue;

            var replacement = accepted.FirstOrDefault(t => !SchemaPrimitiveTypes.IsPrimitive(t) && _registry.GetType(t) is not null);
            if (replacement is not null)
                suggestion.SuggestedNestedSchemaTypeName = replacement;
        }
    }

    /// <summary>
    /// True when <paramref name="nestedType"/> is assignable to any of the property's
    /// <paramref name="acceptedTypes"/>. Uses the actual Schema.NET CLR types: a property narrowed
    /// by Schema.NET to a base type (e.g. <c>recipeInstructions</c> → <c>CreativeWork</c>) still
    /// accepts any subtype that implements that base's interface (<c>HowToStep</c> implements
    /// <c>ICreativeWork</c>) — even though schema.org's multiple inheritance means the CLR class
    /// hierarchy alone (HowToStep : ListItem) would miss it. Unknown nested types are treated as in
    /// range so a curated prior is never second-guessed.
    /// </summary>
    private bool IsWithinRange(string nestedType, IReadOnlyList<string> acceptedTypes)
    {
        var nestedClr = _registry.GetClrType(nestedType);
        if (nestedClr is null)
            return true;

        return acceptedTypes.Any(accepted =>
            !SchemaPrimitiveTypes.IsPrimitive(accepted)
            && (string.Equals(accepted, nestedType, StringComparison.OrdinalIgnoreCase)
                || (_registry.GetClrType(accepted) is { } acceptedClr && acceptedClr.IsAssignableFrom(nestedClr))
                || nestedClr.GetInterfaces().Any(i =>
                    string.Equals(i.Name, "I" + accepted, StringComparison.Ordinal)
                    || string.Equals(i.Name, accepted, StringComparison.Ordinal))));
    }

    private static bool IsRichSourceType(string? sourceType) =>
        string.Equals(sourceType, "complexType", StringComparison.OrdinalIgnoreCase)
        || string.Equals(sourceType, "blockContent", StringComparison.OrdinalIgnoreCase);

    // ---- Detection helpers -----------------------------------------------------------------

    /// <summary>
    /// Returns the single text-bearing property alias of a block element type when the block is a
    /// flat single-field list (one non-block property), else null. This is the deterministic
    /// signal for a string-list mapping — generalising the hard-coded <c>recipeIngredient</c>
    /// preset to any "one text field per row" block.
    /// </summary>
    private static string? DetectSingleTextField(IReadOnlyList<BlockElementTypeInfo> elements)
    {
        if (elements.Count == 0)
            return null;

        string? alias = null;
        foreach (var element in elements)
        {
            var nonBlock = element.PropertyInfos.Count > 0
                ? element.PropertyInfos.Where(p => !BlockEditorAliases.Contains(p.EditorAlias)).Select(p => p.Alias).ToList()
                : element.Properties;

            if (nonBlock.Count != 1)
                return null;

            if (alias is null)
                alias = nonBlock[0];
            else if (!string.Equals(alias, nonBlock[0], StringComparison.OrdinalIgnoreCase))
                return null; // heterogeneous single fields — ambiguous, don't guess
        }

        return alias;
    }

    private static bool AcceptsText(IReadOnlyList<string> acceptedTypes) =>
        acceptedTypes.Any(t => string.Equals(t, "Text", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(t, "String", StringComparison.OrdinalIgnoreCase));

    // ---- camelCase tokenisation ------------------------------------------------------------

    /// <summary>The camelCase prefix (all tokens but the last): <c>locationAddress</c> → <c>location</c>.</summary>
    internal static string CamelPrefix(string alias)
    {
        var tokens = SplitCamel(alias);
        return tokens.Count <= 1 ? alias.ToLowerInvariant() : string.Concat(tokens.Take(tokens.Count - 1)).ToLowerInvariant();
    }

    /// <summary>The final camelCase token: <c>locationAddress</c> → <c>Address</c>.</summary>
    internal static string CamelSuffix(string alias)
    {
        var tokens = SplitCamel(alias);
        return tokens.Count == 0 ? alias : tokens[^1];
    }

    private static List<string> SplitCamel(string value)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(value))
            return tokens;

        var start = 0;
        for (var i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
            {
                tokens.Add(value[start..i]);
                start = i;
            }
        }
        tokens.Add(value[start..]);
        return tokens;
    }

    // ---- resolver-config (de)serialisation -------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static bool HasComplexTypeMappings(string? json) =>
        TryHasArray(json, "complexTypeMappings");

    private static bool IsStringList(string? json)
    {
        using var doc = TryParse(json);
        return doc is not null
               && doc.RootElement.TryGetProperty("extractAs", out var ea)
               && ea.ValueKind == JsonValueKind.String
               && string.Equals(ea.GetString(), "stringList", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryHasArray(string? json, string property)
    {
        using var doc = TryParse(json);
        return doc is not null
               && doc.RootElement.TryGetProperty(property, out var arr)
               && arr.ValueKind == JsonValueKind.Array
               && arr.GetArrayLength() > 0;
    }

    private static JsonDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<NestedBinding> ReadNestedMappings(JsonDocument? doc)
    {
        var result = new List<NestedBinding>();
        if (doc is null || !doc.RootElement.TryGetProperty("nestedMappings", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var el in arr.EnumerateArray())
        {
            var sp = GetString(el, "schemaProperty");
            var cp = GetString(el, "contentProperty");
            if (string.IsNullOrEmpty(sp))
                continue;
            result.Add(new NestedBinding(sp!, cp ?? string.Empty, GetString(el, "wrapInType"), GetString(el, "wrapInProperty")));
        }
        return result;
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string SerialiseComplexType(List<ComplexBinding> bindings) =>
        JsonSerializer.Serialize(new
        {
            complexTypeMappings = bindings.Select(b => new
            {
                schemaProperty = b.SchemaProperty,
                sourceType = "property",
                contentTypePropertyAlias = b.ContentProperty,
            }),
        }, JsonOptions);

    private static string SerialiseStringList(string innerAlias) =>
        JsonSerializer.Serialize(new { extractAs = "stringList", contentProperty = innerAlias }, JsonOptions);

    private static string SerialiseNestedMappings(List<NestedBinding> bindings) =>
        JsonSerializer.Serialize(new
        {
            nestedMappings = bindings.Select(b => new
            {
                schemaProperty = b.SchemaProperty,
                contentProperty = b.ContentProperty,
                wrapInType = b.WrapInType,
                wrapInProperty = b.WrapInProperty,
            }),
        }, JsonOptions);

    private readonly record struct ComplexBinding(string SchemaProperty, string ContentProperty);

    private readonly record struct NestedBinding(string SchemaProperty, string ContentProperty, string? WrapInType, string? WrapInProperty);
}
