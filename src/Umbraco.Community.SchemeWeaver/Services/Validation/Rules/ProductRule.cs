using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for Product / IndividualProduct / ProductModel.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/product"/>.
/// Required: <c>name</c> and at least one of <c>review</c>, <c>aggregateRating</c>
/// or <c>offers</c>. Google treats absence of all three as non-eligible.
/// </summary>
public sealed class ProductRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Product", "IndividualProduct", "ProductModel",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Product";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required for every Product.");

        var hasReview = RuleHelpers.HasNonEmptyArrayOrObject(node, "Review");
        var hasAggregate = RuleHelpers.HasNonEmptyArrayOrObject(node, "AggregateRating");
        var hasOffers = RuleHelpers.HasNonEmptyArrayOrObject(node, "Offers");

        if (!hasReview && !hasAggregate && !hasOffers)
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}",
                "Product must declare at least one of `review`, `aggregateRating` or `offers` to be rich-result eligible.");

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.image",
                "Missing `image` — strongly recommended; Google uses it as the thumbnail in product results.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended for product snippet text.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Brand")
            && !RuleHelpers.HasNonEmptyString(node, "Brand"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.brand",
                "Missing `brand` — recommended (string or Brand/Organization).");

        // Offers price / priceCurrency sanity when offers is present.
        if (hasOffers && RuleHelpers.TryGetField(node, "Offers", out var offers))
        {
            var i = 0;
            foreach (var offer in EnumerateOneOrMany(offers))
            {
                var offerPath = hasMany(offers) ? $"{path}.offers[{i}]" : $"{path}.offers";
                if (!RuleHelpers.HasNonEmptyString(offer, "Price")
                    && !RuleHelpers.HasNonEmptyString(offer, "PriceSpecification"))
                    yield return new ValidationIssue(ValidationSeverity.Critical, type,
                        $"{offerPath}.price",
                        "Offer is missing `price` — required for product-offer rich results.");
                if (!RuleHelpers.HasNonEmptyString(offer, "PriceCurrency"))
                    yield return new ValidationIssue(ValidationSeverity.Critical, type,
                        $"{offerPath}.priceCurrency",
                        "Offer is missing `priceCurrency` — required (3-letter ISO 4217 code).");
                i++;
            }
        }

        static bool hasMany(JsonElement e) => e.ValueKind == JsonValueKind.Array;
    }

    private static IEnumerable<JsonElement> EnumerateOneOrMany(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : new[] { value };
}
