using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class FinancialProductRuleTests
{
    private readonly FinancialProductRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "FinancialProduct",
          "@id": "https://example.com/cards/gold#financialproduct",
          "name": "Example Gold Credit Card",
          "provider": { "@type": "Organization", "name": "Example Bank" },
          "category": "Credit Card",
          "feesAndCommissionsSpecification": "Annual fee £95.",
          "interestRate": 24.9
        }
        """;

    [Fact]
    public void AppliesTo_FinancialProduct_ReturnsTrue() =>
        _sut.AppliesTo("FinancialProduct").Should().BeTrue();

    [Theory]
    [InlineData("Product")]
    [InlineData("FinancialService")]
    [InlineData("Offer")]
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
        var json = FullyPopulated.Replace("\"name\": \"Example Gold Credit Card\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingProvider_ProducesWarning()
    {
        var json = FullyPopulated.Replace(
            "\"provider\": { \"@type\": \"Organization\", \"name\": \"Example Bank\" },",
            string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.provider");
    }

    [Fact]
    public void Check_MissingCategory_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"category\": \"Credit Card\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.category");
    }

    [Fact]
    public void Check_MissingInterestRate_ProducesWarning()
    {
        const string json = """
            {
              "@type": "FinancialProduct",
              "name": "Example Gold Credit Card",
              "provider": { "@type": "Organization", "name": "Example Bank" },
              "category": "Credit Card",
              "feesAndCommissionsSpecification": "Annual fee £95."
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.interestRate");
    }
}
