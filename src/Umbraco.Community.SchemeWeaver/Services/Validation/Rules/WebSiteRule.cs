using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for WebSite. The canonical site node powers
/// the sitelinks search box and brand-name resolution.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/sitelinks-searchbox"/>.
/// </summary>
public sealed class WebSiteRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "WebSite",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "WebSite";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required; used as the site's display name in search features.");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.url",
                "Missing `url` — required; the canonical home URL of the site.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Publisher"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.publisher",
                "Missing `publisher` — recommended (Organization or Person) so Google can associate the site with a publishing entity.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "PotentialAction"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.potentialAction",
                "Missing `potentialAction` — recommended; declare a `SearchAction` with a url-template target to unlock the sitelinks search box.");
    }
}
