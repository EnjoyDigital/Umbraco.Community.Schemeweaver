using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class ProductRuleTests
{
    private readonly ProductRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Product",
          "@id": "https://example.com/products/widget#product",
          "name": "Widget",
          "description": "The finest widget.",
          "image": "https://example.com/images/widget.jpg",
          "brand": { "@type": "Brand", "name": "Acme" },
          "offers": {
            "@type": "Offer",
            "price": "19.99",
            "priceCurrency": "GBP"
          }
        }
        """;

    [Theory]
    [InlineData("Product")]
    [InlineData("IndividualProduct")]
    [InlineData("ProductModel")]
    public void AppliesTo_ProductFamily_ReturnsTrue(string type) => _sut.AppliesTo(type).Should().BeTrue();

    [Fact]
    public void AppliesTo_Article_ReturnsFalse() => _sut.AppliesTo("Article").Should().BeFalse();

    [Fact]
    public void Check_FullyPopulated_ProducesNoIssues()
    {
        _sut.Check(Parse(FullyPopulated), "$").Should().BeEmpty();
    }

    [Fact]
    public void Check_MissingName_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"name\": \"Widget\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.name");
    }

    [Fact]
    public void Check_NoReviewNoAggregateNoOffers_ProducesCritical()
    {
        const string json = """
            {
              "@type": "Product",
              "@id": "https://example.com/products/widget",
              "name": "Widget"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$"
            && i.Message.Contains("review"));
    }

    [Fact]
    public void Check_HasAggregateRating_SatisfiesOneOfRequirement()
    {
        const string json = """
            {
              "@type": "Product",
              "@id": "https://example.com/products/widget",
              "name": "Widget",
              "aggregateRating": { "@type": "AggregateRating", "ratingValue": "4.5", "reviewCount": "123" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotContain(i => i.Message.Contains("at least one of"));
    }

    [Fact]
    public void Check_OfferMissingPrice_ProducesCritical()
    {
        const string json = """
            {
              "@type": "Product",
              "@id": "https://example.com/products/widget",
              "name": "Widget",
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
              "@type": "Product",
              "@id": "https://example.com/products/widget",
              "name": "Widget",
              "offers": { "@type": "Offer", "price": "19.99" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.offers.priceCurrency");
    }

    [Fact]
    public void Check_MissingImage_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"image\": \"https://example.com/images/widget.jpg\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Severity == ValidationSeverity.Warning && i.Path == "$.image");
    }
}
