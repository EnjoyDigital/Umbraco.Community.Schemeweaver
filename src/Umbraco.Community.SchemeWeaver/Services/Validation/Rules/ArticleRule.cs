using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for the Article family
/// (Article / BlogPosting / NewsArticle / TechArticle).
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/article"/>.
/// Google lists <c>headline</c>, <c>image</c>, <c>author</c>, <c>datePublished</c>
/// and <c>dateModified</c> as strongly recommended — treated here as Critical
/// for headline/image/datePublished/author (required in practice to get any
/// rich-result treatment) and Warning for dateModified/publisher/description.
/// </summary>
public sealed class ArticleRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Article", "BlogPosting", "NewsArticle", "TechArticle",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Article";

        if (!RuleHelpers.HasNonEmptyString(node, "Headline"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.headline",
                "Missing `headline` — Google requires it to display the article title in rich results.");

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.image",
                "Missing `image` — Google requires at least one image for article rich results (recommended 16:9, 4:3 and 1:1 variants at ≥1200px wide).");

        if (!RuleHelpers.HasIsoDate(node, "DatePublished"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.datePublished",
                "Missing or non-ISO `datePublished` — required for article eligibility in Top Stories / news carousels.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Author"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.author",
                "Missing `author` — Google requires at least one Person or Organization author for byline display.");

        if (!RuleHelpers.HasIsoDate(node, "DateModified"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.dateModified",
                "Missing `dateModified` — recommended so Google can show freshness signals and re-crawl cadence.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Publisher"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.publisher",
                "Missing `publisher` — recommended (Organization with name + logo). Required historically for AMP Top Stories; still a positive signal.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended; Google uses it for snippet generation when no meta description is set.");
    }
}
