using System.Text.Json.Serialization;

namespace Umbraco.Community.SchemeWeaver.Seo.Templates;

/// <summary>A curated "content kind": a Schema.org type plus the slots an SEO fills in.</summary>
public sealed class SeoTemplate
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Family { get; init; } = "content";
    public string Icon { get; init; } = "icon-document";

    /// <summary>The default Schema.org type written to the mapping.</summary>
    public string SchemaType { get; init; } = string.Empty;

    /// <summary>Subtypes an editor may choose for a content type (wizard) or a single page (Schema tab).</summary>
    public IReadOnlyList<SeoTemplateVariant> Variants { get; init; } = [];

    /// <summary>When true the wizard offers the catalogue's <c>businessTypes</c> list as variants.</summary>
    public bool UseBusinessTypes { get; init; }

    public SeoGoogleFeature GoogleFeature { get; init; } = new();

    /// <summary>Lower-case fragments; a content type alias/name containing one is pre-highlighted.</summary>
    public IReadOnlyList<string> AliasHints { get; init; } = [];

    public IReadOnlyList<SeoSlot> Slots { get; init; } = [];

    [JsonIgnore]
    public bool IsRetired => string.Equals(GoogleFeature.Status, SchemeWeaverSeoConstants.FeatureStatus.Retired, StringComparison.Ordinal);

    /// <summary>Every Schema.org type this template claims: the default type plus its variants.</summary>
    [JsonIgnore]
    public IEnumerable<string> OwnedSchemaTypes =>
        new[] { SchemaType }.Concat(Variants.Select(v => v.SchemaType)).Distinct(StringComparer.OrdinalIgnoreCase);

    public SeoSlot? FindSlot(string schemaProperty) =>
        Slots.FirstOrDefault(s => string.Equals(s.SchemaProperty, schemaProperty, StringComparison.OrdinalIgnoreCase));
}

public sealed class SeoTemplateVariant
{
    public string SchemaType { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
}

public sealed class SeoGoogleFeature
{
    public string Name { get; init; } = string.Empty;
    public string DocUrl { get; init; } = string.Empty;

    /// <summary><c>rich-result</c>, <c>foundation</c> or <c>retired</c>.</summary>
    public string Status { get; init; } = SchemeWeaverSeoConstants.FeatureStatus.RichResult;
}

/// <summary>One field an SEO fills in for a kind. Maps 1:1 onto a Schema.org property of the kind's type.</summary>
public sealed class SeoSlot
{
    public string SchemaProperty { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Help { get; init; } = string.Empty;

    /// <summary><c>required</c>, <c>recommended</c> or <c>optional</c>.</summary>
    public string Importance { get; init; } = SchemeWeaverSeoConstants.Importance.Recommended;

    /// <summary>Value shape: text, image, date, url, number, boolean, duration, enum, list or entity.</summary>
    public string Kind { get; init; } = "text";

    /// <summary>
    /// Umbraco editor aliases offered by default for this slot. Populated by the catalogue
    /// loader from <c>slotKinds</c> when the JSON does not override it.
    /// </summary>
    public IReadOnlyList<string> AcceptedEditorAliases { get; set; } = [];

    /// <summary>Built-in aliases (<c>__name</c>, <c>__url</c>, …) offered for this slot.</summary>
    public IReadOnlyList<string> BuiltIns { get; set; } = [];

    /// <summary>
    /// Google accepts any one slot of a group (Product: offers / review / aggregateRating).
    /// A required any-of slot is satisfied when any member of its group is filled.
    /// </summary>
    public string? AnyOfGroup { get; init; }

    /// <summary>Graph piece this slot may reference instead of mapping a field ("From site identity").</summary>
    public string? SiteIdentityPieceKey { get; init; }

    /// <summary>Applied when nothing better is suggested (built-ins, site identity references).</summary>
    public SeoDefaultSource? DefaultSource { get; init; }

    /// <summary>Fixed options for <c>enum</c> slots (availability, currency, employment type…).</summary>
    public IReadOnlyList<SeoFixedValueOption> FixedValueOptions { get; init; } = [];

    /// <summary>For <c>entity</c> slots: the nested type and its own slots.</summary>
    public SeoNestedTemplate? Nested { get; init; }

    [JsonIgnore]
    public bool IsRequired => string.Equals(Importance, SchemeWeaverSeoConstants.Importance.Required, StringComparison.Ordinal);

    [JsonIgnore]
    public bool IsRecommended => string.Equals(Importance, SchemeWeaverSeoConstants.Importance.Recommended, StringComparison.Ordinal);

    public SeoSlot? FindSubSlot(string schemaProperty) =>
        Nested?.Slots.FirstOrDefault(s => string.Equals(s.SchemaProperty, schemaProperty, StringComparison.OrdinalIgnoreCase));
}

public sealed class SeoDefaultSource
{
    /// <summary><c>property</c> (with <see cref="Alias"/>), <c>reference</c> (with <see cref="TargetPieceKey"/>) or <c>static</c>.</summary>
    public string SourceType { get; init; } = SchemeWeaverConstants.SourceTypes.Property;
    public string? Alias { get; init; }
    public string? TargetPieceKey { get; init; }
    public string? StaticValue { get; init; }
}

public sealed class SeoFixedValueOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class SeoNestedTemplate
{
    public string SchemaType { get; init; } = string.Empty;
    public IReadOnlyList<SeoSlot> Slots { get; init; } = [];
}

public sealed class SeoRetiredFeature
{
    public string SchemaType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RetiredOn { get; init; } = string.Empty;
    public string DocUrl { get; init; } = string.Empty;
}

public sealed class SeoBusinessType
{
    public string SchemaType { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

/// <summary>Editor aliases and built-ins offered per slot kind.</summary>
public sealed class SeoSlotKindDefaults
{
    public IReadOnlyList<string> AcceptedEditorAliases { get; init; } = [];
    public IReadOnlyList<string> BuiltIns { get; init; } = [];
}

/// <summary>On-disk shape of <c>Templates/catalogue.json</c>.</summary>
public sealed class SeoCatalogueDocument
{
    public Dictionary<string, SeoSlotKindDefaults> SlotKinds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SeoTemplate> Templates { get; init; } = [];
    public List<SeoRetiredFeature> Retired { get; init; } = [];
    public List<string> Deferred { get; init; } = [];
    public List<SeoBusinessType> BusinessTypes { get; init; } = [];
}
