using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class VehicleRuleTests
{
    private readonly VehicleRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Car",
          "@id": "https://example.com/cars/v1#vehicle",
          "name": "2024 Example Saloon",
          "image": "https://example.com/images/car.jpg",
          "description": "A lightly-used example car.",
          "brand": { "@type": "Brand", "name": "ExampleMotors" },
          "manufacturer": { "@type": "Organization", "name": "ExampleMotors Ltd" },
          "modelDate": "2024-01-01",
          "vehicleModelDate": "2024-01-01",
          "vehicleIdentificationNumber": "1HGCM82633A004352",
          "mileageFromOdometer": { "@type": "QuantitativeValue", "value": 12000, "unitCode": "SMI" },
          "itemCondition": "https://schema.org/UsedCondition",
          "color": "Silver",
          "fuelType": "Gasoline",
          "bodyType": "Sedan",
          "offers": {
            "@type": "Offer",
            "price": "15999.00",
            "priceCurrency": "GBP"
          }
        }
        """;

    [Theory]
    [InlineData("Vehicle")]
    [InlineData("Car")]
    [InlineData("Motorcycle")]
    public void AppliesTo_VehicleFamily_ReturnsTrue(string type) => _sut.AppliesTo(type).Should().BeTrue();

    [Theory]
    [InlineData("Product")]
    [InlineData("Offer")]
    [InlineData("Article")]
    public void AppliesTo_OtherType_ReturnsFalse(string type) => _sut.AppliesTo(type).Should().BeFalse();

    [Fact]
    public void Check_FullyPopulated_ProducesNoIssues()
    {
        var issues = _sut.Check(Parse(FullyPopulated), "$").ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Check_MissingName_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"name\": \"2024 Example Saloon\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingImage_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"image\": \"https://example.com/images/car.jpg\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.image");
    }

    [Fact]
    public void Check_MissingOffers_ProducesCritical()
    {
        const string json = """
            {
              "@type": "Car",
              "name": "2024 Example Saloon",
              "image": "https://example.com/images/car.jpg"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.offers");
    }

    [Fact]
    public void Check_OfferMissingPrice_ProducesCritical()
    {
        const string json = """
            {
              "@type": "Car",
              "name": "2024 Example Saloon",
              "image": "https://example.com/images/car.jpg",
              "offers": { "@type": "Offer", "priceCurrency": "GBP" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.offers.price");
    }

    [Fact]
    public void Check_OfferMissingPriceCurrency_ProducesCritical()
    {
        const string json = """
            {
              "@type": "Car",
              "name": "2024 Example Saloon",
              "image": "https://example.com/images/car.jpg",
              "offers": { "@type": "Offer", "price": "15999" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.offers.priceCurrency");
    }

    [Fact]
    public void Check_MissingBrand_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"brand\": { \"@type\": \"Brand\", \"name\": \"ExampleMotors\" },", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.brand");
    }

    [Fact]
    public void Check_MissingVin_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"vehicleIdentificationNumber\": \"1HGCM82633A004352\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.vehicleIdentificationNumber");
    }

    [Fact]
    public void Check_MissingMileage_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"mileageFromOdometer\": { \"@type\": \"QuantitativeValue\", \"value\": 12000, \"unitCode\": \"SMI\" },", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.mileageFromOdometer");
    }

    [Fact]
    public void Check_MissingFuelType_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"fuelType\": \"Gasoline\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.fuelType");
    }

    [Fact]
    public void Check_OffersAsArray_IndexesPath()
    {
        const string json = """
            {
              "@type": "Car",
              "name": "2024 Example Saloon",
              "image": "https://example.com/images/car.jpg",
              "offers": [
                { "@type": "Offer", "price": "15999", "priceCurrency": "GBP" },
                { "@type": "Offer", "priceCurrency": "GBP" }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.offers[1].price");
    }
}
