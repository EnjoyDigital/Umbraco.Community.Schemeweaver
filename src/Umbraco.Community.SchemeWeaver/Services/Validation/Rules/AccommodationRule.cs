using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for the Accommodation family (Apartment,
/// ApartmentComplex, GatedResidenceCommunity, House, Residence,
/// SingleFamilyResidence, Suite). Primarily drives Google's Vacation
/// Rental rich result.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/vacation-rental"/>.
/// Critical: <c>name</c>, <c>address</c>. Other fields
/// (image / description / numberOfRooms / occupancy / floorSize /
/// amenityFeature / geo / telephone) are strongly recommended.
/// </summary>
public sealed class AccommodationRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accommodation",
        "Apartment",
        "ApartmentComplex",
        "GatedResidenceCommunity",
        "House",
        "Residence",
        "SingleFamilyResidence",
        "Suite",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Accommodation";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required to title the accommodation in vacation-rental rich results.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Address")
            && !RuleHelpers.HasNonEmptyString(node, "Address"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.address",
                "Missing `address` — required for vacation-rental eligibility (PostalAddress with streetAddress / addressLocality / addressRegion / postalCode, or a full string).");

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.image",
                "Missing `image` — recommended; Google uses it as the thumbnail in vacation-rental listings.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended for the property snippet text.");

        if (!RuleHelpers.TryGetField(node, "NumberOfRooms", out _))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.numberOfRooms",
                "Missing `numberOfRooms` — recommended (integer or QuantitativeValue) so guests can filter by size.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Occupancy"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.occupancy",
                "Missing `occupancy` — recommended (QuantitativeValue) for guest-capacity filtering.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "FloorSize")
            && !RuleHelpers.HasNonEmptyString(node, "FloorSize"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.floorSize",
                "Missing `floorSize` — recommended (QuantitativeValue with unit, e.g. square metres).");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "AmenityFeature"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.amenityFeature",
                "Missing `amenityFeature` — recommended (array of LocationFeatureSpecification) so Google can surface WiFi / parking / pool etc.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Geo"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.geo",
                "Missing `geo` — recommended (GeoCoordinates with latitude/longitude) so Google can pin the property on the map.");

        if (!RuleHelpers.HasNonEmptyString(node, "Telephone"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.telephone",
                "Missing `telephone` — recommended so Google can surface a tap-to-call action on mobile.");
    }
}
