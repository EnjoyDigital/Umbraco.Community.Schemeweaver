using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for LocalBusiness and its many subtypes. The
/// subtype list below covers the common vertical categories (food &amp;
/// drink, retail, automotive, medical, professional services, leisure,
/// lodging, education). Stricter than <see cref="OrganizationRule"/> — a
/// LocalBusiness always needs an address to be rich-result eligible.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/local-business"/>.
/// </summary>
public sealed class LocalBusinessRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "LocalBusiness",
        // Food & drink
        "Restaurant", "BarOrPub", "Bakery", "Brewery", "CafeOrCoffeeShop",
        "FastFoodRestaurant", "Winery",
        // Retail
        "Store", "ClothingStore", "BookStore", "ElectronicsStore",
        "FurnitureStore", "GroceryStore", "JewelryStore",
        // Automotive
        "AutoDealer", "AutoRepair",
        // Medical
        "Dentist", "Physician", "Hospital", "MedicalClinic", "Pharmacy",
        "DiagnosticLab",
        // Professional services
        "AccountingService", "Attorney", "FinancialService", "InsuranceAgency",
        "LegalService", "Notary", "ProfessionalService", "RealEstateAgent",
        "TravelAgency",
        // Leisure / attractions
        "MovieTheater", "NightClub", "ExerciseGym", "GolfCourse", "SkiResort",
        "StadiumOrArena", "AmusementPark", "TouristAttraction", "Zoo",
        "Library", "Museum",
        // Lodging
        "Hotel", "BedAndBreakfast", "LodgingBusiness", "Campground",
        // Civic / education
        "Courthouse", "School", "CollegeOrUniversity",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "LocalBusiness";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — required for every LocalBusiness.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Address")
            && !RuleHelpers.HasNonEmptyString(node, "Address"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.address",
                "Missing `address` — required for local-business rich results (PostalAddress with streetAddress / addressLocality / addressRegion / postalCode, or a full string).");

        if (!RuleHelpers.HasNonEmptyString(node, "Telephone"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.telephone",
                "Missing `telephone` — recommended so Google can surface a tap-to-call action on mobile.");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.url",
                "Missing `url` — recommended; the canonical website for the business listing.");

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.image",
                "Missing `image` — recommended; Google uses it as the thumbnail in local pack / map results.");

        if (!RuleHelpers.HasNonEmptyString(node, "PriceRange"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.priceRange",
                "Missing `priceRange` — recommended (e.g. `$$`, `£10-£20`); shown in local-pack cards.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Geo"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.geo",
                "Missing `geo` — recommended (GeoCoordinates with latitude/longitude) so Google can pin the business on the map.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "OpeningHoursSpecification"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.openingHoursSpecification",
                "Missing `openingHoursSpecification` — recommended so Google can show open/closed status and hours.");
    }
}
