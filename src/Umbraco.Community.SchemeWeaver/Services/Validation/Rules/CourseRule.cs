using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for Course / CourseInstance.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/course"/>.
/// Google requires <c>name</c>, <c>description</c> and <c>provider</c> (with a
/// name). Offers, instances, ratings and a canonical url enrich the result
/// and are required for the Course List experience.
/// </summary>
public sealed class CourseRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Course", "CourseInstance",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Course";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — Google requires it to display the course title in rich results.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.description",
                "Missing `description` — required for Course rich results.");

        // Provider must be an object carrying a name (Organization or Person).
        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Provider"))
        {
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.provider",
                "Missing `provider` — Google requires an Organization (or Person) with a `name`.");
        }
        else if (RuleHelpers.TryGetField(node, "Provider", out var provider)
            && !RuleHelpers.HasNonEmptyString(provider, "Name"))
        {
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.provider.name",
                "`provider` is missing `name` — Google requires the provider organisation/person to be named.");
        }

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Offers"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.offers",
                "Missing `offers` — recommended (price / priceCurrency / category). Required for the Course List paid-course experience.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "HasCourseInstance"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.hasCourseInstance",
                "Missing `hasCourseInstance` — recommended (array of CourseInstance) so Google can show delivery mode, schedule and duration.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "AggregateRating"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.aggregateRating",
                "Missing `aggregateRating` — recommended; required to show star ratings in course rich results.");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.url",
                "Missing `url` — recommended (absolute canonical URL to the course landing page).");
    }
}
