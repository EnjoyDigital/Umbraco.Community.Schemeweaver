using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for generic WebPage and close subtypes
/// (<c>WebPage</c>, <c>CollectionPage</c>, <c>ContactPage</c>). Dedicated
/// rules exist for <see cref="AboutPageRule"/>, <see cref="ProfilePageRule"/>
/// and <see cref="FAQPageRule"/>; this rule covers the long tail.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/article#webpage"/>
/// and the <see href="https://schema.org/WebPage">WebPage schema</see>.
/// Google treats WebPage as a weak rich-result carrier — <c>name</c> and
/// <c>url</c> are required to identify the page; <c>description</c>,
/// <c>image</c>, <c>isPartOf</c> and <c>breadcrumb</c> strengthen knowledge
/// graph linkage.
/// </summary>
public sealed class WebPageRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "WebPage", "CollectionPage", "ContactPage",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "WebPage";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required so Google can display the page title in knowledge-graph and sitelink cards.");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.url",
                "Missing `url` — required; the canonical absolute URL for this page.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended; Google uses it for snippet generation when no meta description is set.");

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
