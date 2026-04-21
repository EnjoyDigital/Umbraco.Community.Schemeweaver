using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for <c>Service</c>. Service is a generic
/// offering carrier (consulting, trades, SaaS) — Google uses it for
/// local-pack enrichment and knowledge-panel disambiguation rather than a
/// dedicated rich-result template, so <c>name</c> is the only Critical
/// field; provider / serviceType / areaServed / description lift the
/// quality signal.
///
/// Rules from <see href="https://schema.org/Service"/>.
/// </summary>
public sealed class ServiceRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Service",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Service";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required so Google can display the service title in knowledge-graph and local-pack cards.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Provider"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.provider",
                "Missing `provider` — recommended (Person or Organization delivering the service) so Google can link the service back to the provider's knowledge panel.");

        if (!RuleHelpers.HasNonEmptyString(node, "ServiceType"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.serviceType",
                "Missing `serviceType` — recommended (e.g. `Plumbing`, `Tax preparation`); used by Google to categorise the service in local results.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "AreaServed")
            && !RuleHelpers.HasNonEmptyString(node, "AreaServed"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.areaServed",
                "Missing `areaServed` — recommended (GeoShape, AdministrativeArea, or plain string) so Google knows where the service is available.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended; used for snippet generation when Google lacks a meta description.");
    }
}
