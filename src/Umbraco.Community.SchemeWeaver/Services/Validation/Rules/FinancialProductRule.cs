using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Generic-eligibility rule for <c>FinancialProduct</c>. Google does not
/// publish a dedicated rich-result format for financial products, so we
/// only require the universally-needed <c>name</c> and flag the contextual
/// fields (provider, category, fees, interest rate) as warnings.
///
/// See Schema.org: <see href="https://schema.org/FinancialProduct"/>.
/// </summary>
public sealed class FinancialProductRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "FinancialProduct",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "FinancialProduct";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required; the title of the financial product.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Provider")
            && !RuleHelpers.HasNonEmptyString(node, "Provider"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.provider",
                "Missing `provider` — recommended (Organization) so consumers can identify the issuing institution.");

        if (!RuleHelpers.HasNonEmptyString(node, "Category")
            && !RuleHelpers.HasNonEmptyArrayOrObject(node, "Category"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.category",
                "Missing `category` — recommended (e.g. `Credit Card`, `Mortgage`, `Savings Account`).");

        if (!RuleHelpers.HasNonEmptyString(node, "FeesAndCommissionsSpecification")
            && !RuleHelpers.HasNonEmptyArrayOrObject(node, "FeesAndCommissionsSpecification"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.feesAndCommissionsSpecification",
                "Missing `feesAndCommissionsSpecification` — recommended; disclose fees and commissions applicable to this product.");

        if (!RuleHelpers.TryGetField(node, "InterestRate", out _))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.interestRate",
                "Missing `interestRate` — recommended (number or QuantitativeValue) for products where interest applies.");
    }
}
