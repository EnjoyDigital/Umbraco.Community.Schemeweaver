using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for FAQPage. The page must expose a non-empty
/// <c>mainEntity</c> array of Question nodes, and every Question must have
/// <c>name</c> (the question) plus an <c>acceptedAnswer</c> whose
/// <c>text</c> is non-empty.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/faqpage"/>.
/// </summary>
public sealed class FAQPageRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "FAQPage",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "FAQPage";

        if (!RuleHelpers.TryGetField(node, "MainEntity", out var mainEntity)
            || !IsNonEmptyArray(mainEntity))
        {
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.mainEntity",
                "Missing `mainEntity` — required; supply a non-empty array of Question nodes.");
            yield break;
        }

        var index = 0;
        foreach (var question in mainEntity.EnumerateArray())
        {
            var qPath = $"{path}.mainEntity[{index}]";

            if (!RuleHelpers.HasNonEmptyString(question, "Name"))
                yield return new ValidationIssue(ValidationSeverity.Critical, type,
                    $"{qPath}.name",
                    "Question is missing `name` — required; this is the question text shown in the rich result.");

            if (!HasAcceptedAnswerWithText(question))
                yield return new ValidationIssue(ValidationSeverity.Critical, type,
                    $"{qPath}.acceptedAnswer",
                    "Question is missing `acceptedAnswer` with non-empty `text` — required; Google uses the answer text verbatim in the FAQ rich result.");

            index++;
        }
    }

    private static bool IsNonEmptyArray(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0;

    private static bool HasAcceptedAnswerWithText(JsonElement question)
    {
        if (!RuleHelpers.TryGetField(question, "AcceptedAnswer", out var answer))
            return false;

        return answer.ValueKind switch
        {
            JsonValueKind.Object => RuleHelpers.HasNonEmptyString(answer, "Text"),
            JsonValueKind.Array => answer.EnumerateArray().Any(e =>
                e.ValueKind == JsonValueKind.Object && RuleHelpers.HasNonEmptyString(e, "Text")),
            _ => false,
        };
    }
}
