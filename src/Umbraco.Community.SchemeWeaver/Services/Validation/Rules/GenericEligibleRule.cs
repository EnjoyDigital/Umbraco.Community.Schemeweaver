using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Always-on rule applied to every node regardless of type. Enforces the
/// bare-minimum fields any JSON-LD Thing needs to be linkable and named.
/// Type-specific rules layer additional requirements on top.
/// </summary>
public sealed class GenericEligibleRule : ITypeRule
{
    public bool AppliesTo(string schemaType) => true;

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.TryGetProperty("@type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? "(unknown)"
            : "(unknown)";

        if (!RuleHelpers.HasUri(node, "@id"))
        {
            yield return new ValidationIssue(
                ValidationSeverity.Warning,
                type,
                $"{path}.@id",
                "Missing or malformed @id — Google recommends a stable, absolute URL identifier per node so pieces can cross-reference.");
        }
    }
}
