using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for <c>ProfilePage</c>. Profile pages are
/// first-class rich results (author carousels, creator search); the rule
/// requires <c>mainEntity</c> (a Person or Organization with a name) and
/// recommends <c>dateCreated</c> / <c>dateModified</c> so Google can
/// attribute freshness to the profile.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/profile-page"/>.
/// </summary>
public sealed class ProfilePageRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProfilePage",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "ProfilePage";

        if (!RuleHelpers.TryGetField(node, "MainEntity", out var mainEntity)
            || mainEntity.ValueKind != JsonValueKind.Object
            || !RuleHelpers.HasNonEmptyString(mainEntity, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.mainEntity",
                "Missing or incomplete `mainEntity` — Google requires a Person or Organization with a `name` so the profile page can be attributed to a creator.");

        if (!RuleHelpers.HasIsoDate(node, "DateCreated"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.dateCreated",
                "Missing or non-ISO `dateCreated` — recommended so Google can order profile pages by creator tenure in author carousels.");

        if (!RuleHelpers.HasIsoDate(node, "DateModified"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.dateModified",
                "Missing or non-ISO `dateModified` — recommended so Google can show freshness signals on the profile.");

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.name",
                "Missing `name` — recommended; the display title of the profile page (often the creator's handle or display name).");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.url",
                "Missing `url` — recommended; the canonical absolute URL for the profile page.");
    }
}
