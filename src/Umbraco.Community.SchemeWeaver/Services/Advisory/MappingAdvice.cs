namespace Umbraco.Community.SchemeWeaver.Services.Advisory;

/// <summary>
/// The kind of advisory a <see cref="IMappingAdvisor"/> can raise. Each maps to a concrete,
/// actionable improvement an author can make to a mapping — never a hard error (those stay
/// <c>warning</c>/<c>critical</c>); advisories surface as <c>suggestion</c> severity and never
/// mutate the saved mapping ("inform + suggest, never auto-apply").
/// </summary>
public enum MappingAdviceKind
{
    /// <summary>An HTML-producing source feeds a plain-text Schema.org property without a transform.</summary>
    StripHtml,

    /// <summary>A block list feeds an ordered-list property (itemListElement) without ListItem wrapping.</summary>
    WrapInListItem,

    /// <summary>A known rich-result nested type (e.g. Question) is missing a required property.</summary>
    MissingRequiredNestedProperty,

    /// <summary>The save reached the database only while uSync is installed — it won't reproduce elsewhere.</summary>
    ExportToUSync,

    /// <summary>A block editor feeds a structured Schema.org property in basic property mode — blockContent would emit real Things.</summary>
    PreferBlockContent,
}

/// <summary>
/// A single advisory. <see cref="Path"/> is the owning <c>SchemaPropertyName</c> so the frontend can
/// key it back to its mapping row, mirroring <c>SchemaRangeValidator</c>. <see cref="Fix"/> is the
/// optional structured pre-fill the suggester can apply; it is <c>null</c> for advisory-only kinds
/// (the author must supply the value — e.g. which block property holds the answer).
/// </summary>
public sealed record MappingAdvice(
    MappingAdviceKind Kind,
    string SchemaType,
    string Path,
    string Message,
    MappingAdviceFix? Fix = null);

/// <summary>
/// The minimal patch the auto-mapper applies onto a suggestion when pre-filling. Advisory-only
/// kinds carry no fix.
/// </summary>
public sealed record MappingAdviceFix(
    string? TransformType = null,
    bool WrapInListItem = false,
    string? PositionProperty = null);

/// <summary>
/// Pure input for the per-property advisory checks (1-4). Carries everything the advisor needs
/// without taking an Umbraco service dependency — the one fact it can't derive, the source
/// property's editor alias, is supplied by the caller (both call sites already hold it).
/// </summary>
public sealed record MappingEntryInput(
    string SchemaTypeName,
    string SchemaPropertyName,
    string SourceType,
    string? ContentEditorAlias = null,
    string? TransformType = null,
    string? NestedSchemaTypeName = null,
    string? ResolverConfig = null);

/// <summary>
/// Pure input for the persistence advisory, computed by the service after a save.
/// </summary>
public sealed record PersistenceFacts(
    string DriftStatus,
    bool USyncAvailable,
    bool ExportOnSaveEnabled);
