using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for VideoObject.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/video"/>.
/// Google requires <c>name</c>, <c>description</c>, <c>thumbnailUrl</c> and an
/// ISO <c>uploadDate</c>. <c>duration</c>, <c>contentUrl</c>, <c>embedUrl</c>
/// and <c>interactionStatistic</c> enrich the video result and are required
/// for Key Moments / Live Badge experiences.
/// </summary>
public sealed class VideoObjectRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "VideoObject",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "VideoObject";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — Google requires it to display the video title in rich results.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.description",
                "Missing `description` — required for video rich results.");

        if (!RuleHelpers.HasUri(node, "ThumbnailUrl")
            && !RuleHelpers.HasNonEmptyString(node, "ThumbnailUrl"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.thumbnailUrl",
                "Missing `thumbnailUrl` — Google requires a thumbnail image URL for video rich results.");

        if (!RuleHelpers.HasIsoDate(node, "UploadDate"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.uploadDate",
                "Missing or non-ISO `uploadDate` — required. Use ISO 8601 with timezone offset (e.g. `2026-06-15T20:00:00-05:00`).");

        if (!RuleHelpers.HasNonEmptyString(node, "Duration"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.duration",
                "Missing `duration` — recommended as an ISO 8601 duration (e.g. `PT1M33S`) so Google can show the video length.");

        if (!RuleHelpers.HasUri(node, "ContentUrl")
            && !RuleHelpers.HasNonEmptyString(node, "ContentUrl"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.contentUrl",
                "Missing `contentUrl` — recommended (direct URL to the video file). At least one of `contentUrl` or `embedUrl` should be present.");

        if (!RuleHelpers.HasUri(node, "EmbedUrl")
            && !RuleHelpers.HasNonEmptyString(node, "EmbedUrl"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.embedUrl",
                "Missing `embedUrl` — recommended (URL of a player for the video). At least one of `contentUrl` or `embedUrl` should be present.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "InteractionStatistic"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.interactionStatistic",
                "Missing `interactionStatistic` — recommended (InteractionCounter with `WatchAction`) so Google can show view counts.");
    }
}
