using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for standalone <c>Offer</c> nodes. <c>Offer</c>
/// also appears nested inside Product / Event / Accommodation / Vehicle —
/// those parent rules validate the embedded offer inline, but when an Offer
/// stands alone in the graph we apply the same required-field checks here.
///
/// Rules follow Google's Product / Merchant guidance:
/// <see href="https://developers.google.com/search/docs/appearance/structured-data/product"/>.
/// Critical: <c>price</c> (or <c>priceSpecification</c>), <c>priceCurrency</c>
/// (ISO 4217), <c>availability</c>.
/// </summary>
public sealed class OfferRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Offer",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Offer";

        if (!RuleHelpers.HasNonEmptyString(node, "Price")
            && !RuleHelpers.HasNonEmptyArrayOrObject(node, "PriceSpecification"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.price",
                "Missing `price` — required (or provide a `priceSpecification` with a price). Use a decimal string without currency symbols (e.g. `19.99`).");

        if (!RuleHelpers.HasNonEmptyString(node, "PriceCurrency"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.priceCurrency",
                "Missing `priceCurrency` — required (3-letter ISO 4217 code, e.g. `GBP`, `USD`).");

        if (!RuleHelpers.HasNonEmptyString(node, "Availability")
            && !RuleHelpers.HasNonEmptyArrayOrObject(node, "Availability"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.availability",
                "Missing `availability` — required (e.g. `https://schema.org/InStock`, `/OutOfStock`, `/PreOrder`).");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.url",
                "Missing `url` — recommended; the deep link to the page where the offer can be purchased.");

        if (!RuleHelpers.HasIsoDate(node, "PriceValidUntil"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.priceValidUntil",
                "Missing `priceValidUntil` — recommended (ISO 8601 date). Google stops showing the price after this date.");

        if (!RuleHelpers.HasNonEmptyString(node, "ItemCondition")
            && !RuleHelpers.HasNonEmptyArrayOrObject(node, "ItemCondition"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.itemCondition",
                "Missing `itemCondition` — recommended (e.g. `https://schema.org/NewCondition`, `/UsedCondition`, `/RefurbishedCondition`).");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Seller")
            && !RuleHelpers.HasNonEmptyString(node, "Seller"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.seller",
                "Missing `seller` — recommended (Organization or Person) so Google can attribute the offer to a merchant.");
    }
}
