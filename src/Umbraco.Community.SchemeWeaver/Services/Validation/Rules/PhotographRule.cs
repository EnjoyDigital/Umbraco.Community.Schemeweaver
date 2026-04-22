using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Rule for <c>Photograph</c>. Google has no dedicated rich result for
/// <c>Photograph</c> (image rich results are driven by <c>ImageObject</c>)
/// but the type is commonly used for editorial photos — we nudge authors
/// toward the licensing / attribution fields Google uses for Image
/// Licensable and Credit surfaces. All checks are warnings only.
///
/// See Schema.org: <see href="https://schema.org/Photograph"/> and Google's
/// Image Licensable docs:
/// <see href="https://developers.google.com/search/docs/appearance/structured-data/image-license-metadata"/>.
/// </summary>
public sealed class PhotographRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Photograph",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Photograph";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.name",
                "Missing `name` — recommended; the title or caption of the photograph.");

        if (!RuleHelpers.HasUri(node, "ContentUrl")
            && !RuleHelpers.HasNonEmptyString(node, "ContentUrl"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.contentUrl",
                "Missing `contentUrl` — recommended; the direct URL to the image binary.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Author")
            && !RuleHelpers.HasNonEmptyString(node, "Author"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.author",
                "Missing `author` — recommended (Person or Organization) for attribution.");

        if (!RuleHelpers.HasIsoDate(node, "DatePublished"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.datePublished",
                "Missing `datePublished` — recommended (ISO 8601) so Google can show when the photograph was taken or published.");

        if (!RuleHelpers.HasUri(node, "License")
            && !RuleHelpers.HasNonEmptyString(node, "License"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.license",
                "Missing `license` — recommended (URL to licence terms) for Image Licensable eligibility.");

        if (!RuleHelpers.HasNonEmptyString(node, "CreditText"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.creditText",
                "Missing `creditText` — recommended; the credit line Google shows alongside the image.");
    }
}
