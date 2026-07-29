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

    /// <summary>
    /// Emit one <see cref="ValidationIssue"/> for each <see cref="FieldRule"/> whose
    /// field is not present in the required form. This is the declarative counterpart
    /// of the long if-chains that rules would otherwise inline: each rule names a field,
    /// a severity, a <see cref="PresenceKind"/> (which presence helper decides "present")
    /// and the message to raise when it is absent. Field lookup stays case-tolerant
    /// (camelCase then PascalCase) via <see cref="TryGetField"/>.
    /// </summary>
    public static IEnumerable<ValidationIssue> CheckFields(
        JsonElement node, string path, string schemaType, IEnumerable<FieldRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!IsPresent(node, rule.Field, rule.Presence))
                yield return new ValidationIssue(
                    rule.Severity, schemaType, $"{path}.{ToCamelCase(rule.Field)}", rule.Message);
        }
    }

    /// <summary>
    /// Validate the <c>offers</c> sub-block for the Vehicle-listing rich result: each
    /// Offer must carry a <c>price</c> (or a <c>priceSpecification</c> object/array) and a
    /// <c>priceCurrency</c>. A single Offer object is pathed as <c>…offers</c>; an array of
    /// Offers is pathed as <c>…offers[i]</c>. Nothing is emitted when <c>offers</c> is absent
    /// or is not a non-empty array/object — its presence is asserted separately as a field.
    /// </summary>
    public static IEnumerable<ValidationIssue> CheckOffers(JsonElement node, string path, string schemaType)
    {
        if (!HasNonEmptyArrayOrObject(node, "Offers") || !TryGetField(node, "Offers", out var offers))
            yield break;

        var many = offers.ValueKind == JsonValueKind.Array;
        var i = 0;
        foreach (var offer in EnumerateOneOrMany(offers))
        {
            var offerPath = many ? $"{path}.offers[{i}]" : $"{path}.offers";
            if (!HasNonEmptyString(offer, "Price")
                && !HasNonEmptyArrayOrObject(offer, "PriceSpecification"))
                yield return new ValidationIssue(ValidationSeverity.Critical, schemaType,
                    $"{offerPath}.price",
                    "Offer is missing `price` — required for vehicle-listing rich results.");
            if (!HasNonEmptyString(offer, "PriceCurrency"))
                yield return new ValidationIssue(ValidationSeverity.Critical, schemaType,
                    $"{offerPath}.priceCurrency",
                    "Offer is missing `priceCurrency` — required (3-letter ISO 4217 code).");
            i++;
        }
    }

    /// <summary>
    /// Decide whether <paramref name="field"/> is present on <paramref name="node"/> in the
    /// form demanded by <paramref name="kind"/>, delegating to the existing presence helpers.
    /// </summary>
    private static bool IsPresent(JsonElement node, string field, PresenceKind kind) => kind switch
    {
        PresenceKind.NonEmptyString => HasNonEmptyString(node, field),
        PresenceKind.StringOrObject => HasNonEmptyString(node, field) || HasNonEmptyArrayOrObject(node, field),
        PresenceKind.ArrayOrObject => HasNonEmptyArrayOrObject(node, field),
        PresenceKind.Image => HasImage(node, ToCamelCase(field)),
        PresenceKind.IsoDateOrString => HasIsoDate(node, field) || HasNonEmptyString(node, field),
        PresenceKind.FieldPresent => TryGetField(node, field, out _),
        _ => false,
    };

    private static IEnumerable<JsonElement> EnumerateOneOrMany(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : new[] { value };
}

/// <summary>
/// How <see cref="RuleHelpers.CheckFields"/> decides a field is "present". Each value maps
/// onto one of the presence helpers <see cref="RuleHelpers"/> already exposes, keeping the
/// polymorphic and case-tolerant lookup rules in one place.
/// </summary>
internal enum PresenceKind
{
    /// <summary>Satisfied by a non-empty string (or Schema.NET array-of-one string).</summary>
    NonEmptyString,

    /// <summary>Satisfied by EITHER a non-empty string OR a non-empty array/object.</summary>
    StringOrObject,

    /// <summary>Satisfied by a non-empty array or an object.</summary>
    ArrayOrObject,

    /// <summary>Satisfied by an image value (URL string, ImageObject, or array of either).</summary>
    Image,

    /// <summary>Satisfied by an ISO 8601 date OR any non-empty string.</summary>
    IsoDateOrString,

    /// <summary>Satisfied merely by the field being present (any value/shape).</summary>
    FieldPresent,
}

/// <summary>
/// A single declarative field-presence check for <see cref="RuleHelpers.CheckFields"/>:
/// the schema field name (PascalCase, resolved case-tolerantly and lower-cased for the
/// reported path), the <see cref="ValidationSeverity"/> to raise, the <see cref="PresenceKind"/>
/// that decides "present", and the message emitted when the field is missing.
/// </summary>
internal readonly record struct FieldRule(
    string Field, ValidationSeverity Severity, PresenceKind Presence, string Message);
