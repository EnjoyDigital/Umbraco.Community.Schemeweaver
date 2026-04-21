using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class OfferRuleTests
{
    private readonly OfferRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Offer",
          "@id": "https://example.com/offers/1#offer",
          "price": "19.99",
          "priceCurrency": "GBP",
          "availability": "https://schema.org/InStock",
          "url": "https://example.com/buy",
          "priceValidUntil": "2026-12-31",
          "itemCondition": "https://schema.org/NewCondition",
          "seller": { "@type": "Organization", "name": "Example Co" }
        }
        """;

    [Fact]
    public void AppliesTo_Offer_ReturnsTrue() => _sut.AppliesTo("Offer").Should().BeTrue();

    [Theory]
    [InlineData("Product")]
    [InlineData("AggregateOffer")]
    [InlineData("Demand")]
    public void AppliesTo_OtherType_ReturnsFalse(string type) => _sut.AppliesTo(type).Should().BeFalse();

    [Fact]
    public void Check_FullyPopulated_ProducesNoIssues()
    {
        var issues = _sut.Check(Parse(FullyPopulated), "$").ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Check_MissingPrice_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"price\": \"19.99\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.price");
    }

    [Fact]
    public void Check_PriceSpecificationSatisfiesPrice()
    {
        const string json = """
            {
              "@type": "Offer",
              "priceSpecification": {
                "@type": "PriceSpecification",
                "price": "19.99",
                "priceCurrency": "GBP"
              },
              "priceCurrency": "GBP",
              "availability": "https://schema.org/InStock"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotContain(i => i.Path == "$.price");
    }

    [Fact]
    public void Check_MissingPriceCurrency_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"priceCurrency\": \"GBP\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.priceCurrency");
    }

    [Fact]
    public void Check_MissingAvailability_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"availability\": \"https://schema.org/InStock\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.availability");
    }

    [Fact]
    public void Check_MissingUrl_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"url\": \"https://example.com/buy\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.url");
    }

    [Fact]
    public void Check_MissingPriceValidUntil_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"priceValidUntil\": \"2026-12-31\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.priceValidUntil");
    }

    [Fact]
    public void Check_MissingSeller_ProducesWarning()
    {
        const string json = """
            {
              "@type": "Offer",
              "price": "19.99",
              "priceCurrency": "GBP",
              "availability": "https://schema.org/InStock",
              "url": "https://example.com/buy",
              "priceValidUntil": "2026-12-31",
              "itemCondition": "https://schema.org/NewCondition"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.seller");
    }
}
