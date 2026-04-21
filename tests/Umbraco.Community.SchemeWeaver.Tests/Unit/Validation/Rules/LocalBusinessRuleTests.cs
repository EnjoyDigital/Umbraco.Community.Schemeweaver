using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class LocalBusinessRuleTests
{
    private readonly LocalBusinessRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Restaurant",
          "@id": "https://example.com/#restaurant",
          "name": "The Test Kitchen",
          "address": {
            "@type": "PostalAddress",
            "streetAddress": "1 High St",
            "addressLocality": "London",
            "postalCode": "SW1A 1AA",
            "addressCountry": "GB"
          },
          "telephone": "+44 20 0000 0000",
          "url": "https://example.com",
          "image": "https://example.com/restaurant.jpg",
          "priceRange": "££",
          "geo": { "@type": "GeoCoordinates", "latitude": 51.5, "longitude": -0.1 },
          "openingHoursSpecification": [
            { "@type": "OpeningHoursSpecification", "dayOfWeek": "Monday", "opens": "09:00", "closes": "17:00" }
          ]
        }
        """;

    [Theory]
    [InlineData("LocalBusiness")]
    [InlineData("Restaurant")]
    [InlineData("Hotel")]
    [InlineData("Store")]
    [InlineData("Dentist")]
    [InlineData("Attorney")]
    [InlineData("MovieTheater")]
    [InlineData("Museum")]
    [InlineData("School")]
    [InlineData("Winery")]
    public void AppliesTo_LocalBusinessFamily_ReturnsTrue(string type)
    {
        _sut.AppliesTo(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("Organization")]
    [InlineData("Person")]
    [InlineData("Article")]
    [InlineData("Event")]
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
        var json = FullyPopulated.Replace("\"name\": \"The Test Kitchen\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingAddress_ProducesCritical()
    {
        var json = """
            {
              "@type": "Restaurant",
              "name": "The Test Kitchen",
              "telephone": "+44 20 0000 0000",
              "url": "https://example.com",
              "image": "https://example.com/restaurant.jpg",
              "priceRange": "££",
              "geo": { "@type": "GeoCoordinates", "latitude": 51.5, "longitude": -0.1 },
              "openingHoursSpecification": [
                { "@type": "OpeningHoursSpecification", "dayOfWeek": "Monday", "opens": "09:00", "closes": "17:00" }
              ]
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
        var json = """
            {
              "@type": "Restaurant",
              "name": "The Test Kitchen",
              "address": "1 High St, London SW1A 1AA",
              "telephone": "+44 20 0000 0000",
              "url": "https://example.com",
              "image": "https://example.com/restaurant.jpg",
              "priceRange": "££",
              "geo": { "@type": "GeoCoordinates", "latitude": 51.5, "longitude": -0.1 },
              "openingHoursSpecification": [
                { "@type": "OpeningHoursSpecification", "dayOfWeek": "Monday", "opens": "09:00", "closes": "17:00" }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotContain(i => i.Path == "$.address");
    }

    [Fact]
    public void Check_MissingTelephone_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"telephone\": \"+44 20 0000 0000\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.telephone");
    }

    [Fact]
    public void Check_MissingPriceRange_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"priceRange\": \"££\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.priceRange");
    }
}
