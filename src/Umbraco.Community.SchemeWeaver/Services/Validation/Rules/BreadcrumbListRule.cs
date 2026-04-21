using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for BreadcrumbList. The list must expose a
/// non-empty <c>itemListElement</c> array of ListItem nodes. Each ListItem
/// needs an integer <c>position</c>, a <c>name</c>, and an <c>item</c>
/// that resolves to a URL — either as a direct URL string or as an object
/// with an <c>@id</c> / <c>url</c> pointing to the target page.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/breadcrumb"/>.
/// </summary>
public sealed class BreadcrumbListRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "BreadcrumbList",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "BreadcrumbList";

        if (!RuleHelpers.TryGetField(node, "ItemListElement", out var items)
            || !IsNonEmptyArray(items))
        {
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.itemListElement",
                "Missing `itemListElement` — required; supply a non-empty array of ListItem nodes.");
            yield break;
        }

        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var itemPath = $"{path}.itemListElement[{index}]";

            if (!HasIntegerPosition(item))
                yield return new ValidationIssue(ValidationSeverity.Critical, type,
                    $"{itemPath}.position",
                    "ListItem is missing integer `position` — required; Google uses it to order the breadcrumb trail.");

            if (!RuleHelpers.HasNonEmptyString(item, "Name"))
                yield return new ValidationIssue(ValidationSeverity.Critical, type,
                    $"{itemPath}.name",
                    "ListItem is missing `name` — required; this is the displayed breadcrumb label.");

            if (!HasItemUrl(item))
                yield return new ValidationIssue(ValidationSeverity.Critical, type,
                    $"{itemPath}.item",
                    "ListItem is missing `item` URL — required; supply either a URL string or an object with `@id` / `url` pointing to the target page.");

            index++;
        }
    }

    private static bool IsNonEmptyArray(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0;

    private static bool HasIntegerPosition(JsonElement item)
    {
        if (!RuleHelpers.TryGetField(item, "Position", out var position))
            return false;

        return position.ValueKind switch
        {
            JsonValueKind.Number => position.TryGetInt32(out _),
            JsonValueKind.String => int.TryParse(position.GetString(), out _),
            _ => false,
        };
    }

    private static bool HasItemUrl(JsonElement listItem)
    {
        if (!RuleHelpers.TryGetField(listItem, "Item", out var item))
            return false;

        return item.ValueKind switch
        {
            JsonValueKind.String => Uri.TryCreate(item.GetString(), UriKind.Absolute, out _),
            JsonValueKind.Object => RuleHelpers.HasUri(item, "@id") || RuleHelpers.HasUri(item, "Url"),
            _ => false,
        };
    }
}
