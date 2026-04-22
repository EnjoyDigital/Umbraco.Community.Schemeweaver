using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for <c>Occupation</c>. Occupation rarely drives
/// a dedicated rich result — Google's closest comparable experience is
/// <c>JobPosting</c> (covered by <see cref="JobPostingRule"/>). This rule
/// therefore issues Warning only, flagging the fields that improve
/// knowledge-graph quality (estimatedSalary, qualifications, skills) for
/// careers / HR content.
///
/// Rules from <see href="https://schema.org/Occupation"/>.
/// </summary>
public sealed class OccupationRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Occupation",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Occupation";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.name",
                "Missing `name` — recommended; the occupation title (e.g. `Software Engineer`). Google uses it to match queries about the role.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "OccupationLocation")
            && !RuleHelpers.HasNonEmptyString(node, "OccupationLocation"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.occupationLocation",
                "Missing `occupationLocation` — recommended (AdministrativeArea or Place) so Google can localise salary / market data.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended; narrative summary of the role used for snippet generation.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "EstimatedSalary")
            && !RuleHelpers.HasNonEmptyString(node, "EstimatedSalary"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.estimatedSalary",
                "Missing `estimatedSalary` — recommended (MonetaryAmountDistribution or MonetaryAmount) so Google can surface salary ranges in career results.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Qualifications")
            && !RuleHelpers.HasNonEmptyString(node, "Qualifications"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.qualifications",
                "Missing `qualifications` — recommended (EducationalOccupationalCredential or text) listing qualifications expected for the role.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Skills")
            && !RuleHelpers.HasNonEmptyString(node, "Skills"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.skills",
                "Missing `skills` — recommended (DefinedTerm or text) listing skills required for the role.");
    }
}
