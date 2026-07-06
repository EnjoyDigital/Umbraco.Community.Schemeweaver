using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

/// <summary>
/// Locks the Vehicle-specific offer contract that the flat-if refactor must preserve:
/// unlike <see cref="ProductRule"/> (which accepts <c>priceSpecification</c> only as a
/// string), <see cref="VehicleRule"/> treats a <c>priceSpecification</c> object OR array
/// as satisfying the per-Offer `price` requirement. Not covered by the existing suite.
/// </summary>
public class VehicleRulePriceSpecificationTests
{
    private readonly VehicleRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Check_OfferWithPriceSpecificationObject_NoPriceIssue()
    {
        const string json = """
            {
              "@type": "Car",
              "name": "2024 Example Saloon",
              "image": "https://example.com/images/car.jpg",
              "offers": {
                "@type": "Offer",
                "priceSpecification": { "@type": "PriceSpecification", "price": "9.99", "priceCurrency": "GBP" },
                "priceCurrency": "GBP"
              }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotContain(i => i.Path == "$.offers.price");
    }

    [Fact]
    public void Check_OfferWithPriceSpecificationArray_NoPriceIssue()
    {
        const string json = """
            {
              "@type": "Car",
              "name": "2024 Example Saloon",
              "image": "https://example.com/images/car.jpg",
              "offers": {
                "@type": "Offer",
                "priceSpecification": [ { "@type": "UnitPriceSpecification", "price": "9.99", "priceCurrency": "GBP" } ],
                "priceCurrency": "GBP"
              }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotContain(i => i.Path == "$.offers.price");
    }

    [Fact]
    public void Check_OfferWithNeitherPriceNorPriceSpecification_YieldsCriticalPrice()
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

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.offers.price");
    }
}
