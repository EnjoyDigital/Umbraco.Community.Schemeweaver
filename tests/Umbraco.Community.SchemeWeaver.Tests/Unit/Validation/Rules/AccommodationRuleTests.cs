using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class AccommodationRuleTests
{
    private readonly AccommodationRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Apartment",
          "@id": "https://example.com/rentals/flat-1#accommodation",
          "name": "Cosy Central Flat",
          "address": {
            "@type": "PostalAddress",
            "streetAddress": "1 High St",
            "addressLocality": "London",
            "postalCode": "SW1A 1AA",
            "addressCountry": "GB"
          },
          "image": "https://example.com/images/flat-1.jpg",
          "description": "A lovely one-bed flat in central London.",
          "numberOfRooms": 2,
          "occupancy": { "@type": "QuantitativeValue", "maxValue": 3 },
          "floorSize": { "@type": "QuantitativeValue", "value": 55, "unitCode": "MTK" },
          "amenityFeature": [
            { "@type": "LocationFeatureSpecification", "name": "WiFi", "value": true }
          ],
          "geo": { "@type": "GeoCoordinates", "latitude": 51.5, "longitude": -0.1 },
          "telephone": "+44 20 0000 0000"
        }
        """;

    [Theory]
    [InlineData("Accommodation")]
    [InlineData("Apartment")]
    [InlineData("ApartmentComplex")]
    [InlineData("GatedResidenceCommunity")]
    [InlineData("House")]
    [InlineData("Residence")]
    [InlineData("SingleFamilyResidence")]
    [InlineData("Suite")]
    public void AppliesTo_AccommodationFamily_ReturnsTrue(string type)
    {
        _sut.AppliesTo(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("Hotel")]
    [InlineData("LodgingBusiness")]
    [InlineData("Product")]
    [InlineData("Article")]
    public void AppliesTo_OtherType_ReturnsFalse(string type)
    {
        _sut.AppliesTo(type).Should().BeFalse();
    }

    [Fact]
    public void Check_FullyPopulated_ProducesNoIssues()
    {
        var issues = _sut.Check(Parse(FullyPopulated), "$").ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Check_MissingName_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"name\": \"Cosy Central Flat\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingAddress_ProducesCritical()
    {
        const string json = """
            {
              "@type": "Apartment",
              "name": "Cosy Central Flat",
              "image": "https://example.com/images/flat-1.jpg",
              "description": "A lovely one-bed flat in central London.",
              "numberOfRooms": 2,
              "occupancy": { "@type": "QuantitativeValue", "maxValue": 3 },
              "floorSize": { "@type": "QuantitativeValue", "value": 55, "unitCode": "MTK" },
              "amenityFeature": [
                { "@type": "LocationFeatureSpecification", "name": "WiFi", "value": true }
              ],
              "geo": { "@type": "GeoCoordinates", "latitude": 51.5, "longitude": -0.1 },
              "telephone": "+44 20 0000 0000"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.address");
    }

    [Fact]
    public void Check_AddressAsString_IsAccepted()
    {
        const string json = """
            {
              "@type": "Apartment",
              "name": "Cosy Central Flat",
              "address": "1 High St, London SW1A 1AA"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotContain(i => i.Path == "$.address");
    }

    [Fact]
    public void Check_MissingImage_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"image\": \"https://example.com/images/flat-1.jpg\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.image");
    }

    [Fact]
    public void Check_MissingNumberOfRooms_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"numberOfRooms\": 2,", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.numberOfRooms");
    }

    [Fact]
    public void Check_MissingOccupancy_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"occupancy\": { \"@type\": \"QuantitativeValue\", \"maxValue\": 3 },", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.occupancy");
    }

    [Fact]
    public void Check_MissingAmenityFeature_ProducesWarning()
    {
        var json = FullyPopulated.Replace(
            """
              "amenityFeature": [
                { "@type": "LocationFeatureSpecification", "name": "WiFi", "value": true }
              ],
            """,
            string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.amenityFeature");
    }
}
