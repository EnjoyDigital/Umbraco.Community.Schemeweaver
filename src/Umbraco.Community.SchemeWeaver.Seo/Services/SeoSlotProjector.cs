using System.Text.Json;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Models.Api;
using Umbraco.Community.SchemeWeaver.Seo.Templates;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;
using static Umbraco.Community.SchemeWeaver.Seo.SchemeWeaverSeoConstants;

namespace Umbraco.Community.SchemeWeaver.Seo.Services;

public sealed class SeoSlotProjector : ISeoSlotProjector
{
    /// <summary>Suggestions below this confidence are not pre-filled (the core's "show" threshold).</summary>
    public const int SuggestionThreshold = 60;

    private static readonly JsonSerializerOptions ConfigJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ------------------------------------------------------------------ Project (rows → slots)

    public SeoProjectionDto Project(
        SeoTemplate template,
        string schemaTypeName,
        SchemaMappingDto? existing,
        IReadOnlyList<PropertyMappingSuggestion>? suggestions,
        IReadOnlyDictionary<string, string>? editorAliasesByProperty,
        bool siteIdentityConfigured)
    {
        var rows = existing?.PropertyMappings ?? [];
        var claimed = new HashSet<PropertyMappingDto>(ReferenceEqualityComparer.Instance);
        var slots = new List<SeoSlotStateDto>(template.Slots.Count);

        foreach (var slot in template.Slots)
        {
            var row = FindRow(rows, slot.SchemaProperty);
            SeoSlotValueDto value;
            if (row is not null)
            {
                claimed.Add(row);
                value = Classify(slot, row, editorAliasesByProperty);
            }
            else
            {
                // Defaults (built-ins, "from site identity") are offered during set-up, where the
                // editor reviews them before saving; a plain projection of a stored mapping stays
                // lossless so re-saving it unchanged never adds rows.
                value = FromSuggestion(slot, suggestions, editorAliasesByProperty)
                    ?? (suggestions is not null ? FromDefault(slot, siteIdentityConfigured) : null)
                    ?? new SeoSlotValueDto();
            }

            slots.Add(new SeoSlotStateDto { Slot = slot, Value = value });
        }

        var advanced = rows.Where(r => !claimed.Contains(r)).ToList();
        var slotNames = new HashSet<string>(template.Slots.Select(s => s.SchemaProperty), StringComparer.OrdinalIgnoreCase);
        var unslotted = (suggestions ?? [])
            .Where(s => !slotNames.Contains(s.SchemaPropertyName) && s.Confidence >= SuggestionThreshold
                        && FindRow(rows, s.SchemaPropertyName) is null)
            .ToList();

        return new SeoProjectionDto
        {
            TemplateId = template.Id,
            SchemaTypeName = schemaTypeName,
            HasExistingMapping = existing is not null,
            Slots = slots,
            AdvancedRows = advanced,
            UnslottedSuggestions = unslotted,
        };
    }

    private static PropertyMappingDto? FindRow(IEnumerable<PropertyMappingDto> rows, string schemaProperty) =>
        rows.FirstOrDefault(r => string.Equals(r.SchemaPropertyName, schemaProperty, StringComparison.OrdinalIgnoreCase));

    /// <summary>Turns a persisted row into a slot value, or <c>advanced</c> when its shape is beyond the slot model.</summary>
    private static SeoSlotValueDto Classify(SeoSlot slot, PropertyMappingDto row, IReadOnlyDictionary<string, string>? editors)
    {
        var sourceType = row.SourceType ?? SchemeWeaverConstants.SourceTypes.Property;
        var isPlain = string.IsNullOrWhiteSpace(row.ResolverConfig) && string.IsNullOrWhiteSpace(row.DynamicRootConfig)
                      && string.IsNullOrWhiteSpace(row.SourceContentTypeAlias)
                      && (row.TransformType is null or "stripHtml");

        switch (sourceType)
        {
            case SchemeWeaverConstants.SourceTypes.Property when isPlain && !string.IsNullOrWhiteSpace(row.ContentTypePropertyAlias):
                // Entity slots: a picker/text property row is still "property" for the editor; the
                // nested type (if any) is implied by the slot.
                return new SeoSlotValueDto
                {
                    Kind = SlotValueKinds.Property,
                    PropertyAlias = row.ContentTypePropertyAlias,
                    EditorAlias = EditorOf(editors, row.ContentTypePropertyAlias),
                    IsAutoMapped = row.IsAutoMapped,
                };

            case SchemeWeaverConstants.SourceTypes.Static when isPlain:
                return new SeoSlotValueDto { Kind = SlotValueKinds.Fixed, FixedValue = row.StaticValue };

            case SchemeWeaverConstants.SourceTypes.Reference when isPlain
                && slot.SiteIdentityPieceKey is not null
                && string.Equals(row.TargetPieceKey, slot.SiteIdentityPieceKey, StringComparison.OrdinalIgnoreCase):
                return new SeoSlotValueDto { Kind = SlotValueKinds.SiteIdentity, TargetPieceKey = row.TargetPieceKey };

            case SchemeWeaverConstants.SourceTypes.ComplexType when slot.Nested is not null
                && string.Equals(row.NestedSchemaTypeName, slot.Nested.SchemaType, StringComparison.OrdinalIgnoreCase)
                && TryClassifyNested(slot, row, editors, out var nested):
                return nested;

            case SchemeWeaverConstants.SourceTypes.BlockContent when slot.Kind is "list" or "entity":
                return new SeoSlotValueDto
                {
                    Kind = SlotValueKinds.Blocks,
                    PropertyAlias = row.ContentTypePropertyAlias,
                    Summary = $"From blocks in '{row.ContentTypePropertyAlias}'",
                };

            default:
                return new SeoSlotValueDto
                {
                    Kind = SlotValueKinds.Advanced,
                    PropertyAlias = row.ContentTypePropertyAlias,
                    Summary = Summarise(row),
                };
        }
    }

    private static bool TryClassifyNested(SeoSlot slot, PropertyMappingDto row, IReadOnlyDictionary<string, string>? editors, out SeoSlotValueDto value)
    {
        value = new SeoSlotValueDto { Kind = SlotValueKinds.Nested };
        ComplexTypeConfigModel? config;
        try
        {
            config = string.IsNullOrWhiteSpace(row.ResolverConfig)
                ? new ComplexTypeConfigModel()
                : JsonSerializer.Deserialize<ComplexTypeConfigModel>(row.ResolverConfig, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return false;
        }

        if (config is null)
            return false;

        foreach (var entry in config.ComplexTypeMappings ?? [])
        {
            var subSlot = slot.FindSubSlot(entry.SchemaProperty);
            // A sub-row the slot does not know, or a shape the simple model cannot show: the
            // whole row stays advanced so nothing is lost on save.
            if (subSlot is null || !string.IsNullOrWhiteSpace(entry.SourceContentTypeAlias))
                return false;

            var sub = ClassifyEntry(subSlot, entry, editors);
            if (sub is null)
                return false;

            value.Nested[subSlot.SchemaProperty] = sub;
        }

        return true;
    }

    /// <summary>One complex-type entry → a sub-slot value; a nested entity (Place.address → PostalAddress) recurses one level.</summary>
    private static SeoSlotValueDto? ClassifyEntry(SeoSlot subSlot, ComplexTypeMappingEntry entry, IReadOnlyDictionary<string, string>? editors)
    {
        switch (entry.SourceType)
        {
            case SchemeWeaverConstants.SourceTypes.Static when string.IsNullOrWhiteSpace(entry.ResolverConfig):
                return new SeoSlotValueDto { Kind = SlotValueKinds.Fixed, FixedValue = entry.StaticValue };

            case SchemeWeaverConstants.SourceTypes.Property when !string.IsNullOrWhiteSpace(entry.ContentTypePropertyAlias) && string.IsNullOrWhiteSpace(entry.ResolverConfig):
                return new SeoSlotValueDto
                {
                    Kind = SlotValueKinds.Property,
                    PropertyAlias = entry.ContentTypePropertyAlias,
                    EditorAlias = EditorOf(editors, entry.ContentTypePropertyAlias),
                };

            case SchemeWeaverConstants.SourceTypes.ComplexType when subSlot.Nested is not null && !string.IsNullOrWhiteSpace(entry.ResolverConfig):
            {
                ComplexTypeConfigModel? config;
                try
                {
                    config = JsonSerializer.Deserialize<ComplexTypeConfigModel>(entry.ResolverConfig, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException)
                {
                    return null;
                }

                if (config is null || !string.Equals(config.SelectedSubType, subSlot.Nested.SchemaType, StringComparison.OrdinalIgnoreCase))
                    return null;

                var nested = new SeoSlotValueDto { Kind = SlotValueKinds.Nested };
                foreach (var inner in config.ComplexTypeMappings ?? [])
                {
                    var innerSlot = subSlot.FindSubSlot(inner.SchemaProperty);
                    if (innerSlot is null || !string.IsNullOrWhiteSpace(inner.SourceContentTypeAlias))
                        return null;
                    var innerValue = ClassifyEntry(innerSlot, inner, editors);
                    if (innerValue is null)
                        return null;
                    nested.Nested[innerSlot.SchemaProperty] = innerValue;
                }

                return nested;
            }

            default:
                return null;
        }
    }

    private static SeoSlotValueDto? FromSuggestion(SeoSlot slot, IReadOnlyList<PropertyMappingSuggestion>? suggestions, IReadOnlyDictionary<string, string>? editors)
    {
        var suggestion = suggestions?
            .Where(s => string.Equals(s.SchemaPropertyName, slot.SchemaProperty, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Confidence)
            .FirstOrDefault();
        if (suggestion is null || suggestion.Confidence < SuggestionThreshold)
            return null;

        switch (suggestion.SuggestedSourceType)
        {
            case SchemeWeaverConstants.SourceTypes.Property when !string.IsNullOrWhiteSpace(suggestion.SuggestedContentTypePropertyAlias):
                return new SeoSlotValueDto
                {
                    Kind = SlotValueKinds.Property,
                    PropertyAlias = suggestion.SuggestedContentTypePropertyAlias,
                    EditorAlias = suggestion.EditorAlias ?? EditorOf(editors, suggestion.SuggestedContentTypePropertyAlias),
                    Confidence = suggestion.Confidence,
                    IsAutoMapped = suggestion.IsAutoMapped,
                };
            case SchemeWeaverConstants.SourceTypes.Reference when slot.SiteIdentityPieceKey is not null
                && string.Equals(suggestion.SuggestedTargetPieceKey, slot.SiteIdentityPieceKey, StringComparison.OrdinalIgnoreCase):
                return new SeoSlotValueDto { Kind = SlotValueKinds.SiteIdentity, TargetPieceKey = suggestion.SuggestedTargetPieceKey, Confidence = suggestion.Confidence };
            case SchemeWeaverConstants.SourceTypes.Static when suggestion.StaticValue is not null:
                return new SeoSlotValueDto { Kind = SlotValueKinds.Fixed, FixedValue = suggestion.StaticValue, Confidence = suggestion.Confidence };
            case SchemeWeaverConstants.SourceTypes.ComplexType when slot.Nested is not null
                && !string.IsNullOrWhiteSpace(suggestion.SuggestedContentTypePropertyAlias)
                && suggestion.SuggestedResolverConfig is null:
                // "Nest this one field": the heuristic suggests Person from an authorName text box.
                return new SeoSlotValueDto
                {
                    Kind = SlotValueKinds.Property,
                    PropertyAlias = suggestion.SuggestedContentTypePropertyAlias,
                    EditorAlias = suggestion.EditorAlias ?? EditorOf(editors, suggestion.SuggestedContentTypePropertyAlias),
                    Confidence = suggestion.Confidence,
                    IsAutoMapped = suggestion.IsAutoMapped,
                };
            default:
                return null;
        }
    }

    private static SeoSlotValueDto? FromDefault(SeoSlot slot, bool siteIdentityConfigured)
    {
        var d = slot.DefaultSource;
        if (d is null)
            return null;

        return d.SourceType switch
        {
            SchemeWeaverConstants.SourceTypes.Property when !string.IsNullOrWhiteSpace(d.Alias) =>
                new SeoSlotValueDto { Kind = SlotValueKinds.Property, PropertyAlias = d.Alias, EditorAlias = SchemeWeaverConstants.BuiltInProperties.EditorAlias, IsAutoMapped = true },
            SchemeWeaverConstants.SourceTypes.Reference when siteIdentityConfigured && !string.IsNullOrWhiteSpace(d.TargetPieceKey) =>
                new SeoSlotValueDto { Kind = SlotValueKinds.SiteIdentity, TargetPieceKey = d.TargetPieceKey },
            SchemeWeaverConstants.SourceTypes.Static when d.StaticValue is not null =>
                new SeoSlotValueDto { Kind = SlotValueKinds.Fixed, FixedValue = d.StaticValue },
            _ => null,
        };
    }

    // ------------------------------------------------------------------ ToMappingDto (slots → rows)

    public SchemaMappingDto ToMappingDto(
        SeoTemplate template,
        string contentTypeAlias,
        Guid contentTypeKey,
        string schemaTypeName,
        SeoMappingSaveRequest request,
        SchemaMappingDto? existing,
        IReadOnlyDictionary<string, string>? editorAliasesByProperty)
    {
        var existingRows = existing?.PropertyMappings ?? [];
        var rows = new List<PropertyMappingDto>();
        // System.Text.Json rebuilds dictionaries with the default comparer; slot keys are matched
        // case-insensitively (the catalogue says camelCase, clients may not).
        var requested = CaseInsensitive(request.Slots);

        foreach (var slot in template.Slots)
        {
            var current = FindRow(existingRows, slot.SchemaProperty);
            requested.TryGetValue(slot.SchemaProperty, out var value);
            var row = BuildRow(slot, value, current, editorAliasesByProperty);
            if (row is not null)
                rows.Add(row);
        }

        // Everything the template does not own survives untouched (developer additions).
        var owned = new HashSet<string>(template.Slots.Select(s => s.SchemaProperty), StringComparer.OrdinalIgnoreCase);
        rows.AddRange(existingRows.Where(r => !owned.Contains(r.SchemaPropertyName)));

        return new SchemaMappingDto
        {
            ContentTypeAlias = contentTypeAlias,
            ContentTypeKey = existing?.ContentTypeKey is { } k && k != Guid.Empty ? k : contentTypeKey,
            SchemaTypeName = schemaTypeName,
            IsEnabled = existing?.IsEnabled ?? true,
            IsInherited = existing?.IsInherited ?? false,
            IdOverride = existing?.IdOverride,
            PropertyMappings = rows,
        };
    }

    private static PropertyMappingDto? BuildRow(SeoSlot slot, SeoSlotValueDto? value, PropertyMappingDto? current, IReadOnlyDictionary<string, string>? editors)
    {
        var kind = value?.Kind ?? SlotValueKinds.None;
        switch (kind)
        {
            case SlotValueKinds.None:
                return null;

            case SlotValueKinds.Blocks:
            case SlotValueKinds.Advanced:
                // Read-only in the SEO UI: keep whatever is there.
                return current;

            case SlotValueKinds.Fixed:
                return new PropertyMappingDto
                {
                    SchemaPropertyName = slot.SchemaProperty,
                    SourceType = SchemeWeaverConstants.SourceTypes.Static,
                    StaticValue = value!.FixedValue ?? string.Empty,
                };

            case SlotValueKinds.SiteIdentity:
                return new PropertyMappingDto
                {
                    SchemaPropertyName = slot.SchemaProperty,
                    SourceType = SchemeWeaverConstants.SourceTypes.Reference,
                    TargetPieceKey = value!.TargetPieceKey ?? slot.SiteIdentityPieceKey ?? "organization",
                };

            case SlotValueKinds.Property:
            {
                var alias = value!.PropertyAlias?.Trim();
                if (string.IsNullOrEmpty(alias))
                    return null;

                // Same field as before: keep the developer's row (drill config, transforms…).
                if (current is { SourceType: SchemeWeaverConstants.SourceTypes.Property } && string.Equals(current.ContentTypePropertyAlias, alias, StringComparison.Ordinal))
                    return current;

                var editor = value.EditorAlias ?? EditorOf(editors, alias);
                if (slot.Nested is not null && !IsPicker(editor) && !SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(alias))
                {
                    // A single text field feeding an entity slot: nest it as {name} (Person from authorName).
                    var nameSlot = slot.Nested.Slots.FirstOrDefault(s => s.SchemaProperty.Equals("name", StringComparison.OrdinalIgnoreCase))
                                   ?? slot.Nested.Slots.FirstOrDefault();
                    var entry = new ComplexTypeMappingEntry
                    {
                        SchemaProperty = nameSlot?.SchemaProperty ?? "name",
                        SourceType = SchemeWeaverConstants.SourceTypes.Property,
                        ContentTypePropertyAlias = alias,
                        TransformType = NeedsStripHtml(editor) ? "stripHtml" : null,
                    };
                    return new PropertyMappingDto
                    {
                        SchemaPropertyName = slot.SchemaProperty,
                        SourceType = SchemeWeaverConstants.SourceTypes.ComplexType,
                        NestedSchemaTypeName = slot.Nested.SchemaType,
                        ResolverConfig = SerialiseConfig([entry]),
                        IsAutoMapped = value.IsAutoMapped,
                    };
                }

                return new PropertyMappingDto
                {
                    SchemaPropertyName = slot.SchemaProperty,
                    SourceType = SchemeWeaverConstants.SourceTypes.Property,
                    ContentTypePropertyAlias = alias,
                    NestedSchemaTypeName = slot.Nested is not null && IsPicker(editor) ? slot.Nested.SchemaType : null,
                    TransformType = slot.Kind == "text" && NeedsStripHtml(editor) ? "stripHtml" : null,
                    IsAutoMapped = value.IsAutoMapped,
                };
            }

            case SlotValueKinds.Nested:
            {
                if (slot.Nested is null)
                    return null;

                var entries = BuildEntries(slot.Nested, value!.Nested, editors);
                if (entries.Count == 0)
                    return null;

                return new PropertyMappingDto
                {
                    SchemaPropertyName = slot.SchemaProperty,
                    SourceType = SchemeWeaverConstants.SourceTypes.ComplexType,
                    NestedSchemaTypeName = slot.Nested.SchemaType,
                    ResolverConfig = SerialiseConfig(entries),
                };
            }

            default:
                return current;
        }
    }

    /// <summary>Sub-slot values → complex-type entries; a nested entity sub-value becomes an entry with its own config (one more level).</summary>
    private static Dictionary<string, SeoSlotValueDto> CaseInsensitive(IReadOnlyDictionary<string, SeoSlotValueDto>? source)
    {
        var map = new Dictionary<string, SeoSlotValueDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source ?? new Dictionary<string, SeoSlotValueDto>())
            map[key] = value;
        return map;
    }

    private static List<ComplexTypeMappingEntry> BuildEntries(SeoNestedTemplate nested, IReadOnlyDictionary<string, SeoSlotValueDto> rawValues, IReadOnlyDictionary<string, string>? editors)
    {
        var values = CaseInsensitive(rawValues);
        var entries = new List<ComplexTypeMappingEntry>();
        foreach (var subSlot in nested.Slots)
        {
            if (!values.TryGetValue(subSlot.SchemaProperty, out var sub) || sub is null)
                continue;

            switch (sub.Kind)
            {
                case SlotValueKinds.Fixed:
                    entries.Add(new ComplexTypeMappingEntry
                    {
                        SchemaProperty = subSlot.SchemaProperty,
                        SourceType = SchemeWeaverConstants.SourceTypes.Static,
                        StaticValue = sub.FixedValue ?? string.Empty,
                    });
                    break;
                case SlotValueKinds.Property when !string.IsNullOrWhiteSpace(sub.PropertyAlias):
                    var subEditor = sub.EditorAlias ?? EditorOf(editors, sub.PropertyAlias);
                    entries.Add(new ComplexTypeMappingEntry
                    {
                        SchemaProperty = subSlot.SchemaProperty,
                        SourceType = SchemeWeaverConstants.SourceTypes.Property,
                        ContentTypePropertyAlias = sub.PropertyAlias.Trim(),
                        TransformType = subSlot.Kind == "text" && NeedsStripHtml(subEditor) ? "stripHtml" : null,
                    });
                    break;
                case SlotValueKinds.Nested when subSlot.Nested is not null:
                    var inner = BuildEntries(subSlot.Nested, sub.Nested, editors);
                    if (inner.Count == 0)
                        break;
                    entries.Add(new ComplexTypeMappingEntry
                    {
                        SchemaProperty = subSlot.SchemaProperty,
                        SourceType = SchemeWeaverConstants.SourceTypes.ComplexType,
                        ResolverConfig = JsonSerializer.Serialize(new ComplexTypeConfigModel { SelectedSubType = subSlot.Nested.SchemaType, ComplexTypeMappings = inner }, ConfigJson),
                    });
                    break;
            }
        }

        return entries;
    }

    // ------------------------------------------------------------------ Summarise

    public SeoMappingSummaryDto Summarise(SeoTemplate template, SchemaMappingDto mapping)
    {
        var rows = mapping.PropertyMappings;
        var filled = new HashSet<string>(rows.Select(r => r.SchemaPropertyName), StringComparer.OrdinalIgnoreCase);
        var owned = new HashSet<string>(template.Slots.Select(s => s.SchemaProperty), StringComparer.OrdinalIgnoreCase);

        var required = template.Slots.Where(s => s.IsRequired).ToList();
        var recommended = template.Slots.Where(s => s.IsRecommended).ToList();

        // An any-of group counts once, and is satisfied by any member (required or not).
        var groups = template.Slots.Where(s => s.AnyOfGroup is not null).GroupBy(s => s.AnyOfGroup!).ToList();
        var missingRequired = new List<string>();
        var requiredTotal = 0;
        var requiredFilled = 0;
        foreach (var slot in required.Where(s => s.AnyOfGroup is null))
        {
            requiredTotal++;
            if (filled.Contains(slot.SchemaProperty)) requiredFilled++; else missingRequired.Add(slot.SchemaProperty);
        }

        foreach (var group in groups.Where(g => g.Any(s => s.IsRequired)))
        {
            requiredTotal++;
            if (group.Any(s => filled.Contains(s.SchemaProperty))) requiredFilled++;
            else missingRequired.Add(string.Join(" / ", group.Select(s => s.SchemaProperty)));
        }

        var recommendedTotal = recommended.Count(s => s.AnyOfGroup is null);
        var recommendedFilled = recommended.Count(s => s.AnyOfGroup is null && filled.Contains(s.SchemaProperty));

        return new SeoMappingSummaryDto
        {
            SchemaTypeName = mapping.SchemaTypeName,
            TemplateId = template.Id,
            TemplateLabel = template.Label,
            IsRetired = template.IsRetired,
            IsEnabled = mapping.IsEnabled,
            RequiredFilled = requiredFilled,
            RequiredTotal = requiredTotal,
            RecommendedFilled = recommendedFilled,
            RecommendedTotal = recommendedTotal,
            AdvancedRowCount = rows.Count(r => !owned.Contains(r.SchemaPropertyName)),
            MissingRequired = missingRequired,
        };
    }

    // ------------------------------------------------------------------ helpers

    private static string? EditorOf(IReadOnlyDictionary<string, string>? editors, string? alias) =>
        alias is not null && editors is not null && editors.TryGetValue(alias, out var editor) ? editor : null;

    private static bool IsPicker(string? editorAlias) =>
        editorAlias is not null && SchemeWeaverConstants.PropertyEditors.ContentPickerAliases.Contains(editorAlias);

    private static bool NeedsStripHtml(string? editorAlias) =>
        editorAlias is not null && SchemeWeaverConstants.PropertyEditors.HtmlProducingEditorAliases.Contains(editorAlias);

    private static string SerialiseConfig(List<ComplexTypeMappingEntry> entries) =>
        JsonSerializer.Serialize(new ComplexTypeConfigModel { ComplexTypeMappings = entries }, ConfigJson);

    private static string Summarise(PropertyMappingDto row)
    {
        var source = row.SourceType ?? SchemeWeaverConstants.SourceTypes.Property;
        var target = row.ContentTypePropertyAlias ?? row.TargetPieceKey ?? row.StaticValue ?? string.Empty;
        var nested = row.NestedSchemaTypeName is { Length: > 0 } n ? $" as {n}" : string.Empty;
        return $"{source}: {target}{nested}";
    }
}
