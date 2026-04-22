using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Rule for <c>RealEstateListing</c>. Google treats this as a Place-style
/// listing rather than its own rich result; in SchemeWeaver v13 it often
/// resolves to an <c>Offer</c>-shaped payload. We check liberally — only
/// <c>name</c> is critical, everything else (image / description / address /
/// offers / datePosted) is a warning so authors get nudged without being
/// blocked.
///
/// See Schema.org: <see href="https://schema.org/RealEstateListing"/>.
/// </summary>
public sealed class RealEstateListingRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "RealEstateListing",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "RealEstateListing";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required; the title of the real-estate listing.");

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.image",
                "Missing `image` — recommended; Google uses it as the thumbnail in listing results.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended for the listing snippet text.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Address")
            && !RuleHelpers.HasNonEmptyString(node, "Address"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.address",
                "Missing `address` — recommended (PostalAddress or full string) so the property can be located.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Offers"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.offers",
                "Missing `offers` — recommended (Offer with price / priceCurrency) so Google can surface the asking price.");

        if (!RuleHelpers.HasIsoDate(node, "DatePosted"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.datePosted",
                "Missing `datePosted` — recommended (ISO 8601) so Google can show listing freshness.");
    }
}
