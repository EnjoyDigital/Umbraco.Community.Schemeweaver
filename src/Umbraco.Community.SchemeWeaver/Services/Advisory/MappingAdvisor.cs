using System.Text.Json;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Services.Advisory;

/// <inheritdoc />
public sealed class MappingAdvisor : IMappingAdvisor
{
    private readonly ISchemaTypeRegistry _registry;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Properties where HTML content is legitimate, so a <c>stripHtml</c> suggestion would be wrong
    /// (e.g. <c>articleBody</c>; <c>text</c> on Answer/HowToStep accepts limited HTML).
    /// </summary>
    private static readonly HashSet<string> HtmlAllowedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "articleBody",
        "text",
    };

    /// <summary>Ordered-list target properties keyed by name (complements the ListItem-range check).</summary>
    private static readonly HashSet<string> OrderedListProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "itemListElement",
    };

    /// <summary>
    /// Required Schema.org properties for known rich-result nested types. Kept small and conservative,
    /// and aligned with the runtime <c>FAQPageRule</c> (Question needs name + acceptedAnswer). NOTE: this
    /// advisory only checks the property is <em>mapped</em>; clearing it does NOT guarantee
    /// <c>FAQPageRule</c> passes, which additionally requires the resolved <c>acceptedAnswer.text</c> to be
    /// non-empty at render time. It catches the common "forgot to map acceptedAnswer" mistake, not an
    /// empty answer value.
    /// </summary>
    private static readonly Dictionary<string, string[]> RequiredNestedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Question"] = ["name", "acceptedAnswer"],
    };

    public MappingAdvisor(ISchemaTypeRegistry registry) => _registry = registry;

    public IReadOnlyList<MappingAdvice> AdviseEntry(MappingEntryInput entry)
    {
        var advices = new List<MappingAdvice>();
        if (entry is null
            || string.IsNullOrWhiteSpace(entry.SchemaTypeName)
            || string.IsNullOrWhiteSpace(entry.SchemaPropertyName))
            return advices;

        var schemaProps = _registry.GetProperties(entry.SchemaTypeName).ToList();
        if (schemaProps.Count == 0)
            return advices;

        var targetProp = schemaProps.FirstOrDefault(p =>
            string.Equals(p.Name, entry.SchemaPropertyName, StringComparison.OrdinalIgnoreCase));

        var config = ParseConfig(entry.ResolverConfig);

        // Check 1 — an HTML-producing source feeds a plain-text target with no transform.
        if (targetProp is not null
            && string.Equals(entry.SourceType, "property", StringComparison.OrdinalIgnoreCase)
            && entry.ContentEditorAlias is { } editor
            && SchemeWeaverConstants.PropertyEditors.HtmlProducingEditorAliases.Contains(editor)
            && SchemaPrimitiveTypes.IsPlainTextRange(targetProp.AcceptedTypes)
            && !string.Equals(entry.TransformType, "stripHtml", StringComparison.OrdinalIgnoreCase)
            && !HtmlAllowedProperties.Contains(entry.SchemaPropertyName))
        {
            advices.Add(new MappingAdvice(
                MappingAdviceKind.StripHtml, entry.SchemaTypeName, entry.SchemaPropertyName,
                $"'{entry.SchemaPropertyName}' is fed by a rich-text editor ({editor}) but accepts plain text — " +
                "it will emit raw HTML. Set transformType:'stripHtml' to emit clean text.",
                new MappingAdviceFix(TransformType: "stripHtml")));
        }

        // Check 2 — a block list feeds an ordered-list property without ListItem wrapping.
        if (targetProp is not null
            && string.Equals(entry.SourceType, "blockContent", StringComparison.OrdinalIgnoreCase)
            && IsOrderedListProperty(entry.SchemaPropertyName, targetProp.AcceptedTypes)
            && config is not null
            && !string.Equals(config.ExtractAs, "stringList", StringComparison.OrdinalIgnoreCase)
            && HasRoutes(config)
            && !config.WrapInListItem)
        {
            advices.Add(new MappingAdvice(
                MappingAdviceKind.WrapInListItem, entry.SchemaTypeName, entry.SchemaPropertyName,
                $"'{entry.SchemaPropertyName}' is an ordered list but its blocks are not wrapped as ListItems — " +
                "they emit without positions. Set wrapInListItem:true (optionally positionProperty) for an ordered ItemList.",
                new MappingAdviceFix(WrapInListItem: true)));
        }

        // Check 3 — a known rich-result nested type is missing a required property.
        if (config is not null)
        {
            if (config.Routes is { Count: > 0 } routes)
            {
                foreach (var route in routes.Where(r => !string.IsNullOrWhiteSpace(r.NestedSchemaType)))
                {
                    AddMissingRequiredForRoute(entry, route.NestedSchemaType!, route.BlockAlias,
                        route.PropertyMappings?.Select(m => m.SchemaProperty), advices);
                }
            }
            else if (config.NestedMappings is { Count: > 0 } legacy
                     && !string.IsNullOrWhiteSpace(entry.NestedSchemaTypeName))
            {
                AddMissingRequiredForRoute(entry, entry.NestedSchemaTypeName!, blockAlias: null,
                    legacy.Select(m => m.SchemaProperty), advices);
            }
        }

        return advices;
    }

    public MappingAdvice? AdvisePersistence(string schemaTypeName, PersistenceFacts facts)
    {
        if (facts is null || !facts.USyncAvailable)
            return null;
        if (string.Equals(facts.DriftStatus, MappingDriftStatus.InSync, StringComparison.Ordinal))
            return null;

        var isDrifted = string.Equals(facts.DriftStatus, MappingDriftStatus.DbOnly, StringComparison.Ordinal)
            || string.Equals(facts.DriftStatus, MappingDriftStatus.ContentDiffers, StringComparison.Ordinal);
        if (!isDrifted)
            return null;

        var message = facts.ExportOnSaveEnabled
            ? $"Saved, but the on-disk uSync config still differs (drift: {facts.DriftStatus}). " +
              "Run export-mappings-to-usync to reconcile it."
            : "Saved to the database only — it won't reproduce to other environments. " +
              "Enable ExportMappingsToUSyncOnSave or run export-mappings-to-usync.";

        return new MappingAdvice(MappingAdviceKind.ExportToUSync, schemaTypeName, "(persistence)", message);
    }

    /// <summary>
    /// Emits one advisory per required property of <paramref name="nestedType"/> that the route's
    /// mappings do not cover. Advisory-only (no fix) — the author must choose which block property
    /// supplies the value.
    /// </summary>
    private static void AddMissingRequiredForRoute(
        MappingEntryInput entry,
        string nestedType,
        string? blockAlias,
        IEnumerable<string?>? mappedProperties,
        List<MappingAdvice> advices)
    {
        if (!RequiredNestedProperties.TryGetValue(nestedType, out var required))
            return;

        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (mappedProperties is not null)
        {
            foreach (var p in mappedProperties.Where(p => !string.IsNullOrWhiteSpace(p)))
                covered.Add(p!);
        }

        var prefix = string.IsNullOrWhiteSpace(blockAlias) ? string.Empty : $"Block '{blockAlias}': ";

        foreach (var requiredProp in required.Where(p => !covered.Contains(p)))
        {
            advices.Add(new MappingAdvice(
                MappingAdviceKind.MissingRequiredNestedProperty, entry.SchemaTypeName, entry.SchemaPropertyName,
                $"{prefix}{nestedType} does not map '{requiredProp}', which Google's rich result requires — " +
                $"map it on the route (e.g. the block property holding the {requiredProp})."));
        }
    }

    private static bool IsOrderedListProperty(string schemaPropertyName, IReadOnlyList<string> acceptedTypes)
        => OrderedListProperties.Contains(schemaPropertyName)
           || acceptedTypes.Any(t => string.Equals(t, "ListItem", StringComparison.OrdinalIgnoreCase));

    private static bool HasRoutes(ResolverConfigModel config)
        => config.Routes is { Count: > 0 } || config.NestedMappings is { Count: > 0 };

    private static ResolverConfigModel? ParseConfig(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ResolverConfigModel>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
