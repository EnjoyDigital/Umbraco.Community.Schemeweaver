using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for the Vehicle family (Vehicle, Car,
/// Motorcycle). Drives Google's Vehicle Listing rich result.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/vehicle-listing"/>.
/// Critical: <c>name</c>, <c>image</c>, and <c>offers</c> with price + priceCurrency.
/// Everything else (brand, manufacturer, model dates, VIN, mileage, condition,
/// colour, fuel type, body type) is strongly recommended.
/// </summary>
public sealed class VehicleRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Vehicle", "Car", "Motorcycle",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Vehicle";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required to title the vehicle in listing rich results.");

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.image",
                "Missing `image` — required for vehicle-listing rich results; Google uses it as the thumbnail.");

        var hasOffers = RuleHelpers.HasNonEmptyArrayOrObject(node, "Offers");
        if (!hasOffers)
        {
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.offers",
                "Missing `offers` — required (Offer with `price` and `priceCurrency`) for vehicle-listing rich results.");
        }
        else if (RuleHelpers.TryGetField(node, "Offers", out var offers))
        {
            var many = offers.ValueKind == JsonValueKind.Array;
            var i = 0;
            foreach (var offer in EnumerateOneOrMany(offers))
            {
                var offerPath = many ? $"{path}.offers[{i}]" : $"{path}.offers";
                if (!RuleHelpers.HasNonEmptyString(offer, "Price")
                    && !RuleHelpers.HasNonEmptyArrayOrObject(offer, "PriceSpecification"))
                    yield return new ValidationIssue(ValidationSeverity.Critical, type,
                        $"{offerPath}.price",
                        "Offer is missing `price` — required for vehicle-listing rich results.");
                if (!RuleHelpers.HasNonEmptyString(offer, "PriceCurrency"))
                    yield return new ValidationIssue(ValidationSeverity.Critical, type,
                        $"{offerPath}.priceCurrency",
                        "Offer is missing `priceCurrency` — required (3-letter ISO 4217 code).");
                i++;
            }
        }

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended for the vehicle snippet text.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Brand")
            && !RuleHelpers.HasNonEmptyString(node, "Brand"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.brand",
                "Missing `brand` — recommended (string or Brand/Organization, e.g. the make of the vehicle).");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Manufacturer")
            && !RuleHelpers.HasNonEmptyString(node, "Manufacturer"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.manufacturer",
                "Missing `manufacturer` — recommended (Organization) so Google can attribute the vehicle to its maker.");

        if (!RuleHelpers.HasIsoDate(node, "ModelDate")
            && !RuleHelpers.HasNonEmptyString(node, "ModelDate"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.modelDate",
                "Missing `modelDate` — recommended; the release date of the model.");

        if (!RuleHelpers.HasIsoDate(node, "VehicleModelDate")
            && !RuleHelpers.HasNonEmptyString(node, "VehicleModelDate"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.vehicleModelDate",
                "Missing `vehicleModelDate` — recommended; the model year of the specific vehicle.");

        if (!RuleHelpers.HasNonEmptyString(node, "VehicleIdentificationNumber"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.vehicleIdentificationNumber",
                "Missing `vehicleIdentificationNumber` — recommended (17-character VIN) for individual-vehicle listings.");

        if (!RuleHelpers.TryGetField(node, "MileageFromOdometer", out _))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.mileageFromOdometer",
                "Missing `mileageFromOdometer` — recommended (QuantitativeValue with unit) for used vehicles.");

        if (!RuleHelpers.HasNonEmptyString(node, "ItemCondition")
            && !RuleHelpers.HasNonEmptyArrayOrObject(node, "ItemCondition"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.itemCondition",
                "Missing `itemCondition` — recommended (e.g. `https://schema.org/NewCondition`, `/UsedCondition`).");

        if (!RuleHelpers.HasNonEmptyString(node, "Color"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.color",
                "Missing `color` — recommended; helps users filter by exterior colour.");

        if (!RuleHelpers.HasNonEmptyString(node, "FuelType")
            && !RuleHelpers.HasNonEmptyArrayOrObject(node, "FuelType"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.fuelType",
                "Missing `fuelType` — recommended (e.g. `Gasoline`, `Diesel`, `Electric`).");

        if (!RuleHelpers.HasNonEmptyString(node, "BodyType")
            && !RuleHelpers.HasNonEmptyArrayOrObject(node, "BodyType"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.bodyType",
                "Missing `bodyType` — recommended (e.g. `SUV`, `Sedan`, `Hatchback`).");
    }

    private static IEnumerable<JsonElement> EnumerateOneOrMany(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : new[] { value };
}
