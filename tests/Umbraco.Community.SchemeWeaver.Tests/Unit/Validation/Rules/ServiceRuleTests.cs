using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class ServiceRuleTests
{
    private readonly ServiceRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Service",
          "@id": "https://example.com/services/tax#service",
          "name": "Personal Tax Preparation",
          "description": "Self-assessment returns prepared by qualified accountants.",
          "serviceType": "Tax preparation",
          "provider": { "@type": "Organization", "name": "Example Accountants" },
          "areaServed": { "@type": "AdministrativeArea", "name": "Greater London" }
        }
        """;

    [Theory]
    [InlineData("Service")]
    public void AppliesTo_Service_ReturnsTrue(string type)
    {
        _sut.AppliesTo(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("Product")]
    [InlineData("LocalBusiness")]
    [InlineData("Offer")]
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
        var json = FullyPopulated.Replace("\"name\": \"Personal Tax Preparation\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingProvider_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"provider\": { \"@type\": \"Organization\", \"name\": \"Example Accountants\" },", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.provider");
    }

    [Fact]
    public void Check_MissingAreaServed_ProducesWarning()
    {
        var json = """
            {
              "@type": "Service",
              "@id": "https://example.com/services/tax#service",
              "name": "Personal Tax Preparation",
              "description": "Self-assessment returns prepared by qualified accountants.",
              "serviceType": "Tax preparation",
              "provider": { "@type": "Organization", "name": "Example Accountants" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.areaServed");
    }
}
