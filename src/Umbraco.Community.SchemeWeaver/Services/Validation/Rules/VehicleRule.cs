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

    /// <summary>
    /// Declarative table of the flat field-presence checks. Order is presentational only —
    /// each row yields at most one issue, keyed on its own path. The offers sub-block
    /// (per-Offer price/priceCurrency) is validated separately by <see cref="RuleHelpers.CheckOffers"/>.
    /// </summary>
    private static readonly FieldRule[] Fields =
    {
        new("Name", ValidationSeverity.Critical, PresenceKind.NonEmptyString,
            "Missing `name` — required to title the vehicle in listing rich results."),
        new("Image", ValidationSeverity.Critical, PresenceKind.Image,
            "Missing `image` — required for vehicle-listing rich results; Google uses it as the thumbnail."),
        new("Offers", ValidationSeverity.Critical, PresenceKind.ArrayOrObject,
            "Missing `offers` — required (Offer with `price` and `priceCurrency`) for vehicle-listing rich results."),
        new("Description", ValidationSeverity.Warning, PresenceKind.NonEmptyString,
            "Missing `description` — recommended for the vehicle snippet text."),
        new("Brand", ValidationSeverity.Warning, PresenceKind.StringOrObject,
            "Missing `brand` — recommended (string or Brand/Organization, e.g. the make of the vehicle)."),
        new("Manufacturer", ValidationSeverity.Warning, PresenceKind.StringOrObject,
            "Missing `manufacturer` — recommended (Organization) so Google can attribute the vehicle to its maker."),
        new("ModelDate", ValidationSeverity.Warning, PresenceKind.IsoDateOrString,
            "Missing `modelDate` — recommended; the release date of the model."),
        new("VehicleModelDate", ValidationSeverity.Warning, PresenceKind.IsoDateOrString,
            "Missing `vehicleModelDate` — recommended; the model year of the specific vehicle."),
        new("VehicleIdentificationNumber", ValidationSeverity.Warning, PresenceKind.NonEmptyString,
            "Missing `vehicleIdentificationNumber` — recommended (17-character VIN) for individual-vehicle listings."),
        new("MileageFromOdometer", ValidationSeverity.Warning, PresenceKind.FieldPresent,
            "Missing `mileageFromOdometer` — recommended (QuantitativeValue with unit) for used vehicles."),
        new("ItemCondition", ValidationSeverity.Warning, PresenceKind.StringOrObject,
            "Missing `itemCondition` — recommended (e.g. `https://schema.org/NewCondition`, `/UsedCondition`)."),
        new("Color", ValidationSeverity.Warning, PresenceKind.NonEmptyString,
            "Missing `color` — recommended; helps users filter by exterior colour."),
        new("FuelType", ValidationSeverity.Warning, PresenceKind.StringOrObject,
            "Missing `fuelType` — recommended (e.g. `Gasoline`, `Diesel`, `Electric`)."),
        new("BodyType", ValidationSeverity.Warning, PresenceKind.StringOrObject,
            "Missing `bodyType` — recommended (e.g. `SUV`, `Sedan`, `Hatchback`)."),
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Vehicle";

        foreach (var issue in RuleHelpers.CheckFields(node, path, type, Fields))
            yield return issue;

        foreach (var issue in RuleHelpers.CheckOffers(node, path, type))
            yield return issue;
    }
}
