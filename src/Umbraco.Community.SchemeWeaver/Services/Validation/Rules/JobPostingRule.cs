using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for JobPosting. Populated job postings appear
/// in the dedicated Jobs experience; missing required fields drop the
/// posting from that index.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/job-posting"/>.
/// Note that <c>jobLocation</c> may be absent when the role is remote and
/// <c>applicantLocationRequirements</c> + <c>jobLocationType: "TELECOMMUTE"</c>
/// are provided instead.
/// </summary>
public sealed class JobPostingRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "JobPosting",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "JobPosting";

        if (!RuleHelpers.HasNonEmptyString(node, "Title"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.title",
                "Missing `title` — required; the job title shown in Google Jobs cards.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.description",
                "Missing `description` — required; Google expects the full HTML job description.");

        if (!RuleHelpers.HasIsoDate(node, "DatePosted"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.datePosted",
                "Missing or non-ISO `datePosted` — required (ISO 8601, e.g. `2026-04-01`).");

        if (!RuleHelpers.HasIsoDate(node, "ValidThrough"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.validThrough",
                "Missing or non-ISO `validThrough` — required; postings without an expiry date are dropped from Google Jobs once stale.");

        if (!HasHiringOrganizationWithName(node))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.hiringOrganization",
                "Missing `hiringOrganization` — required; supply an Organization object with at least `name`.");

        if (!HasValidJobLocation(node))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.jobLocation",
                "Missing `jobLocation` — required for on-site roles. Remote roles may omit it only when `applicantLocationRequirements` plus `jobLocationType: \"TELECOMMUTE\"` are both provided.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "BaseSalary"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.baseSalary",
                "Missing `baseSalary` — recommended (MonetaryAmount with currency and QuantitativeValue); salary filters rely on it.");

        if (!RuleHelpers.HasNonEmptyString(node, "EmploymentType")
            && !RuleHelpers.HasNonEmptyArrayOrObject(node, "EmploymentType"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.employmentType",
                "Missing `employmentType` — recommended (FULL_TIME, PART_TIME, CONTRACTOR, TEMPORARY, INTERN, VOLUNTEER, PER_DIEM, OTHER).");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Identifier")
            && !RuleHelpers.HasNonEmptyString(node, "Identifier"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.identifier",
                "Missing `identifier` — recommended (PropertyValue with your internal job ID) so updates and duplicates can be reconciled.");
    }

    private static bool HasHiringOrganizationWithName(JsonElement node)
    {
        if (!RuleHelpers.TryGetField(node, "HiringOrganization", out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.Object => RuleHelpers.HasNonEmptyString(value, "Name"),
            JsonValueKind.Array => value.EnumerateArray().Any(e =>
                e.ValueKind == JsonValueKind.Object && RuleHelpers.HasNonEmptyString(e, "Name")),
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            _ => false,
        };
    }

    private static bool HasValidJobLocation(JsonElement node)
    {
        if (RuleHelpers.HasNonEmptyArrayOrObject(node, "JobLocation"))
            return true;

        // Remote-role exemption: applicantLocationRequirements + jobLocationType: TELECOMMUTE.
        var hasApplicantRequirements = RuleHelpers.HasNonEmptyArrayOrObject(node, "ApplicantLocationRequirements");
        var isTelecommute = RuleHelpers.TryGetField(node, "JobLocationType", out var locType)
            && IsTelecommute(locType);

        return hasApplicantRequirements && isTelecommute;
    }

    private static bool IsTelecommute(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => string.Equals(value.GetString(), "TELECOMMUTE", StringComparison.OrdinalIgnoreCase),
        JsonValueKind.Array => value.EnumerateArray().Any(IsTelecommute),
        _ => false,
    };
}
