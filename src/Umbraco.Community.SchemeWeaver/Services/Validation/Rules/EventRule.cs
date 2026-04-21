using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for Event and its subtypes (BusinessEvent,
/// EducationEvent, MusicEvent, SportsEvent, TheaterEvent, ScreeningEvent,
/// Festival, FoodEvent). Applies to the common required fields — event
/// subtypes share the same rich-result requirements.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/event"/>.
/// </summary>
public sealed class EventRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Event", "BusinessEvent", "EducationEvent", "MusicEvent", "SportsEvent",
        "TheaterEvent", "ScreeningEvent", "Festival", "FoodEvent",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Event";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required for event rich results.");

        if (!RuleHelpers.HasIsoDate(node, "StartDate"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.startDate",
                "Missing or non-ISO `startDate` — required. Use ISO 8601 with timezone offset (`2026-06-15T20:00:00-05:00`) for accurate local-time rendering.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Location"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.location",
                "Missing `location` — required. Physical events need Place with address; virtual events need VirtualLocation with a url.");

        if (!RuleHelpers.HasIsoDate(node, "EndDate"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.endDate",
                "Missing `endDate` — recommended so Google can show event duration and filter past events.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended for event snippet display.");

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.image",
                "Missing `image` — recommended; Google uses it as the event thumbnail.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Offers"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.offers",
                "Missing `offers` — recommended (ticket price / availability / url). Required for paid-event rich results with pricing.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Performer"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.performer",
                "Missing `performer` — recommended (Person or Organization).");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Organizer"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.organizer",
                "Missing `organizer` — recommended (Person or Organization).");
    }
}
