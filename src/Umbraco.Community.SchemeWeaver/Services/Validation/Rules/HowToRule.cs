using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for HowTo.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/how-to"/>.
/// Google requires <c>name</c> and a non-empty <c>step</c> array; each step
/// should carry at least a <c>name</c> or <c>text</c> so the step is
/// displayable. Timing, cost, supplies and tools enrich the result.
/// </summary>
public sealed class HowToRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "HowTo",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "HowTo";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — Google requires it to display the how-to title in rich results.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Step"))
        {
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.step",
                "Missing `step` — Google requires a non-empty array of HowToStep entries for how-to rich results.");
        }
        else if (RuleHelpers.TryGetField(node, "Step", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var step in steps.EnumerateArray())
            {
                if (!RuleHelpers.HasNonEmptyString(step, "Name")
                    && !RuleHelpers.HasNonEmptyString(step, "Text"))
                    yield return new ValidationIssue(ValidationSeverity.Warning, type,
                        $"{path}.step[{i}]",
                        "Step is missing both `name` and `text` — at least one is required to render the step.");
                i++;
            }
        }

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.image",
                "Missing `image` — recommended; Google uses it as the how-to thumbnail.");

        if (!RuleHelpers.HasNonEmptyString(node, "TotalTime"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.totalTime",
                "Missing `totalTime` — recommended as an ISO 8601 duration (e.g. `PT30M`) so Google can show a time estimate.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "EstimatedCost")
            && !RuleHelpers.HasNonEmptyString(node, "EstimatedCost"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.estimatedCost",
                "Missing `estimatedCost` — recommended (MonetaryAmount with currency + value).");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Supply"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.supply",
                "Missing `supply` — recommended (array of HowToSupply) to list consumable items the user needs.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Tool"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.tool",
                "Missing `tool` — recommended (array of HowToTool) to list reusable tools the user needs.");
    }
}
