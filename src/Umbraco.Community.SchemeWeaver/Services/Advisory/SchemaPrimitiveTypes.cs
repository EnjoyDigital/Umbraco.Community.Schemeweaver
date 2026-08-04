namespace Umbraco.Community.SchemeWeaver.Services.Advisory;

/// <summary>
/// Shared knowledge of which Schema.org accepted-type entries are scalar/primitive (have no Thing
/// CLR type) versus structured. Extracted so the reactive advisor and the proactive suggester agree
/// on "is this a plain-text property" — the same set <c>SchemaAutoMapper.GetFirstNonPrimitiveAcceptedType</c>
/// uses, kept here as the single source.
/// </summary>
public static class SchemaPrimitiveTypes
{
    private static readonly HashSet<string> Primitives = new(StringComparer.OrdinalIgnoreCase)
    {
        "Text", "String", "Number", "Boolean", "Date", "DateTime", "Time", "URL", "Integer", "Float", "Duration",
    };

    private static readonly HashSet<string> TextLike = new(StringComparer.OrdinalIgnoreCase)
    {
        "Text", "String",
    };

    /// <summary>True when the accepted-type entry is a Schema.org scalar (no structured Thing type).</summary>
    public static bool IsPrimitive(string acceptedType) => Primitives.Contains(acceptedType);

    /// <summary>
    /// True when the property's range is plain text: it accepts at least one text-like type and
    /// EVERY accepted entry is primitive (so the property does not also accept a structured type).
    /// This is the target shape for a <c>stripHtml</c> suggestion — an HTML value here would emit raw
    /// markup as a string.
    /// </summary>
    public static bool IsPlainTextRange(IReadOnlyList<string> acceptedTypes)
    {
        if (acceptedTypes is null || acceptedTypes.Count == 0)
            return false;

        return acceptedTypes.All(IsPrimitive) && acceptedTypes.Any(TextLike.Contains);
    }

    /// <summary>
    /// True when the property's range accepts a plain text value AT ALL (unlike
    /// <see cref="IsPlainTextRange"/>, a range that also accepts structured types still counts).
    /// Used to decide whether an unconfigured content-picker sub-row — which renders the picked
    /// node's NAME — is emitting something the target could legitimately hold.
    /// </summary>
    public static bool AcceptsText(IReadOnlyList<string> acceptedTypes)
        => acceptedTypes is { Count: > 0 } && acceptedTypes.Any(TextLike.Contains);
}
