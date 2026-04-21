using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for <c>AboutPage</c>. Treated as a WebPage
/// (<c>name</c> + <c>url</c> Critical) with an extra Warning when
/// <c>description</c> is missing — Google pulls About page copy into the
/// organisation / person knowledge panel when an explicit description is
/// present.
///
/// Rules from <see href="https://schema.org/AboutPage"/> and the general
/// <see href="https://developers.google.com/search/docs/appearance/structured-data/article#webpage">WebPage</see>
/// guidance.
/// </summary>
public sealed class AboutPageRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "AboutPage",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "AboutPage";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required so Google can display the About page title in knowledge-graph and sitelink cards.");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.url",
                "Missing `url` — required; the canonical absolute URL for this About page.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended on AboutPage; Google uses it as source copy for organisation / person knowledge panels when present.");

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.image",
                "Missing `image` — recommended so Google can show a representative thumbnail in rich results.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "IsPartOf")
            && !RuleHelpers.HasUri(node, "IsPartOf"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.isPartOf",
                "Missing `isPartOf` — recommended (reference to the parent `WebSite` node) so Google can associate the page with your site-level SearchAction and publisher.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Breadcrumb"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.breadcrumb",
                "Missing `breadcrumb` — recommended (reference to a `BreadcrumbList`) so Google can render breadcrumb trails in search results.");
    }
}
