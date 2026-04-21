using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Field-presence helpers shared across rule implementations. Schema.NET
/// emits camelCase by default but Google's docs reference PascalCase — the
/// helpers check camelCase first (matching emitted output) and fall back to
/// PascalCase so the rules can be written against Google's canonical names.
/// </summary>
internal static class RuleHelpers
{
    /// <summary>
    /// Locate a field on the node, trying camelCase then PascalCase.
    /// Returns <see cref="JsonValueKind.Undefined"/> when absent.
    /// </summary>
    public static bool TryGetField(JsonElement node, string fieldName, out JsonElement value)
    {
        value = default;
        if (node.ValueKind != JsonValueKind.Object)
            return false;

        var camel = ToCamelCase(fieldName);
        if (node.TryGetProperty(camel, out value))
            return true;

        if (camel != fieldName && node.TryGetProperty(fieldName, out value))
            return true;

        return false;
    }

    /// <summary>
    /// True when a string field is present and not whitespace.
    /// Accepts plain strings and Schema.NET's array-of-one shape
    /// (<c>"name":["foo"]</c> — Schema.NET wraps values with <c>Values&lt;T&gt;</c>).
    /// </summary>
    public static bool HasNonEmptyString(JsonElement node, string fieldName)
    {
        if (!TryGetField(node, fieldName, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.EnumerateArray().Any(e =>
                e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())),
            _ => false,
        };
    }

    /// <summary>
    /// True when a field is a non-empty array (or non-null object for single-item cases).
    /// </summary>
    public static bool HasNonEmptyArrayOrObject(JsonElement node, string fieldName)
    {
        if (!TryGetField(node, fieldName, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.Array => value.GetArrayLength() > 0,
            JsonValueKind.Object => true,
            _ => false,
        };
    }

    /// <summary>
    /// True when a field can be parsed as an absolute URI. Handles plain
    /// strings, single-element arrays, and object-with-@id shapes (e.g.
    /// <c>"image":{"@type":"ImageObject","@id":"..."}</c>).
    /// </summary>
    public static bool HasUri(JsonElement node, string fieldName)
    {
        if (!TryGetField(node, fieldName, out var value))
            return false;

        return ExtractUriCandidates(value).Any(s => Uri.TryCreate(s, UriKind.Absolute, out _));
    }

    /// <summary>
    /// True when an image field is present. Image may be a URL string, a
    /// single <c>ImageObject</c>, or an array of either.
    /// </summary>
    public static bool HasImage(JsonElement node, string fieldName = "image")
    {
        if (!TryGetField(node, fieldName, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Object => HasUri(value, "@id") || HasUri(value, "url") || HasNonEmptyString(value, "url"),
            JsonValueKind.Array => value.EnumerateArray().Any(el => HasImage(el, "url")
                || (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString()))
                || HasUri(el, "@id")),
            _ => false,
        };
    }

    /// <summary>
    /// True when a date/dateTime field parses as ISO 8601.
    /// </summary>
    public static bool HasIsoDate(JsonElement node, string fieldName)
    {
        if (!TryGetField(node, fieldName, out var value))
            return false;

        var candidate = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array when value.GetArrayLength() > 0 => value[0].GetString(),
            _ => null,
        };

        return !string.IsNullOrWhiteSpace(candidate)
            && DateTimeOffset.TryParse(candidate, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out _);
    }

    public static string ToCamelCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal) || char.IsLower(pascal[0]))
            return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    private static IEnumerable<string?> ExtractUriCandidates(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                yield return value.GetString();
                break;
            case JsonValueKind.Array:
                foreach (var el in value.EnumerateArray())
                    foreach (var s in ExtractUriCandidates(el))
                        yield return s;
                break;
            case JsonValueKind.Object:
                if (value.TryGetProperty("@id", out var id) && id.ValueKind == JsonValueKind.String)
                    yield return id.GetString();
                if (value.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                    yield return url.GetString();
                break;
        }
    }
}
