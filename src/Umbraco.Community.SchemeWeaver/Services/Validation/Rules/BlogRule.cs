using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for <c>Blog</c> and <c>LiveBlogPosting</c>.
/// The Blog node is the index / container (distinct from individual
/// BlogPosting articles, which are covered by <see cref="ArticleRule"/>).
/// LiveBlogPosting adds three live-coverage fields Google uses to drive
/// the "Live" badge in news results.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/article"/>
/// and <see href="https://schema.org/LiveBlogPosting"/>.
/// </summary>
public sealed class BlogRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Blog", "LiveBlogPosting",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Blog";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required so Google can display the blog title in rich results and sitelinks.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended; Google uses it for snippet generation on the blog index.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Author"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.author",
                "Missing `author` — recommended (Person or Organization) so Google can attribute the blog to a creator.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Publisher"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.publisher",
                "Missing `publisher` — recommended (Organization with name + logo). Required historically for AMP Top Stories; still a positive signal.");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.url",
                "Missing `url` — recommended; the canonical absolute URL for the blog index.");

        if (string.Equals(type, "LiveBlogPosting", StringComparison.OrdinalIgnoreCase))
        {
            if (!RuleHelpers.HasIsoDate(node, "CoverageStartTime"))
                yield return new ValidationIssue(ValidationSeverity.Warning, type,
                    $"{path}.coverageStartTime",
                    "Missing or non-ISO `coverageStartTime` — recommended on LiveBlogPosting so Google knows when live coverage began (drives the \"Live\" badge).");

            if (!RuleHelpers.HasIsoDate(node, "CoverageEndTime"))
                yield return new ValidationIssue(ValidationSeverity.Warning, type,
                    $"{path}.coverageEndTime",
                    "Missing or non-ISO `coverageEndTime` — recommended on LiveBlogPosting so Google can retire the \"Live\" badge when coverage ends.");

            if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "LiveBlogUpdate"))
                yield return new ValidationIssue(ValidationSeverity.Warning, type,
                    $"{path}.liveBlogUpdate",
                    "Missing `liveBlogUpdate` — recommended on LiveBlogPosting (array of BlogPosting updates). Google surfaces these as individual headlines in the Top Stories live carousel.");
        }
    }
}
