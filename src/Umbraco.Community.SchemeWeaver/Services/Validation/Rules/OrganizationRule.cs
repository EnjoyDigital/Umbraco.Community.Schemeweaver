using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for the Organization family
/// (Organization / Corporation / NGO / EducationalOrganization /
/// GovernmentOrganization / SportsTeam / Airline / MusicGroup).
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/organization"/>.
/// Google treats <c>name</c> as required; <c>url</c>, <c>logo</c>, <c>sameAs</c>,
/// <c>description</c> and <c>address</c> as strongly recommended for the
/// organisation knowledge panel / brand SERP. LocalBusiness and its subtypes
/// are intentionally excluded — they have their own, stricter rule.
/// </summary>
public sealed class OrganizationRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Organization", "Corporation", "NGO", "EducationalOrganization",
        "GovernmentOrganization", "SportsTeam", "Airline", "MusicGroup",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Organization";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required for every Organization to appear in knowledge panels or brand SERP features.");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.url",
                "Missing `url` — recommended; Google uses the canonical organisation URL to consolidate brand signals.");

        if (!RuleHelpers.HasImage(node, "logo"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.logo",
                "Missing `logo` — recommended (ImageObject or URL); Google shows it in knowledge panels and next to article bylines.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "SameAs")
            && !RuleHelpers.HasNonEmptyString(node, "SameAs"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.sameAs",
                "Missing `sameAs` — recommended (array of social/profile URLs) so Google can link the organisation to its verified external presences.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended for organisation snippet / knowledge panel text.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Address")
            && !RuleHelpers.HasNonEmptyString(node, "Address"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.address",
                "Missing `address` — recommended (PostalAddress or string); helps disambiguate the organisation geographically.");
    }
}
