using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class RealEstateListingRuleTests
{
    private readonly RealEstateListingRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "RealEstateListing",
          "@id": "https://example.com/listings/1#realestatelisting",
          "name": "3-bed semi in Acacia Avenue",
          "image": "https://example.com/images/house.jpg",
          "description": "A spacious 3-bed semi with garden.",
          "address": {
            "@type": "PostalAddress",
            "streetAddress": "22 Acacia Avenue",
            "addressLocality": "Coventry",
            "postalCode": "CV1 2AB",
            "addressCountry": "GB"
          },
          "offers": { "@type": "Offer", "price": "350000", "priceCurrency": "GBP" },
          "datePosted": "2026-04-01"
        }
        """;

    [Fact]
    public void AppliesTo_RealEstateListing_ReturnsTrue() =>
        _sut.AppliesTo("RealEstateListing").Should().BeTrue();

    [Theory]
    [InlineData("Offer")]
    [InlineData("Product")]
    [InlineData("Accommodation")]
    public void AppliesTo_OtherType_ReturnsFalse(string type) =>
        _sut.AppliesTo(type).Should().BeFalse();

    [Fact]
    public void Check_FullyPopulated_ProducesNoIssues()
    {
        var issues = _sut.Check(Parse(FullyPopulated), "$").ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Check_MissingName_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"name\": \"3-bed semi in Acacia Avenue\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingImage_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"image\": \"https://example.com/images/house.jpg\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.image");
    }

    [Fact]
    public void Check_MissingAddress_ProducesWarning()
    {
        const string json = """
            {
              "@type": "RealEstateListing",
              "name": "3-bed semi in Acacia Avenue",
              "image": "https://example.com/images/house.jpg",
              "description": "A spacious 3-bed semi with garden.",
              "offers": { "@type": "Offer", "price": "350000", "priceCurrency": "GBP" },
              "datePosted": "2026-04-01"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.address");
    }

    [Fact]
    public void Check_MissingOffers_ProducesWarning()
    {
        var json = FullyPopulated.Replace(
            "\"offers\": { \"@type\": \"Offer\", \"price\": \"350000\", \"priceCurrency\": \"GBP\" },",
            string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.offers");
    }

    [Fact]
    public void Check_MissingDatePosted_ProducesWarning()
    {
        const string json = """
            {
              "@type": "RealEstateListing",
              "name": "3-bed semi in Acacia Avenue",
              "image": "https://example.com/images/house.jpg",
              "description": "A spacious 3-bed semi with garden.",
              "address": { "@type": "PostalAddress", "streetAddress": "22 Acacia Avenue" },
              "offers": { "@type": "Offer", "price": "350000", "priceCurrency": "GBP" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.datePosted");
    }
}
