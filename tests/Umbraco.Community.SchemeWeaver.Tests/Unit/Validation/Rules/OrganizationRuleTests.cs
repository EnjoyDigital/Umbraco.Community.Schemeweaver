using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class OrganizationRuleTests
{
    private readonly OrganizationRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Organization",
          "@id": "https://example.com/#org",
          "name": "Example Co",
          "url": "https://example.com",
          "logo": { "@type": "ImageObject", "url": "https://example.com/logo.png" },
          "sameAs": [
            "https://twitter.com/example",
            "https://www.linkedin.com/company/example"
          ],
          "description": "We do things.",
          "address": { "@type": "PostalAddress", "streetAddress": "1 High St", "addressLocality": "London" }
        }
        """;

    [Theory]
    [InlineData("Organization")]
    [InlineData("Corporation")]
    [InlineData("NGO")]
    [InlineData("EducationalOrganization")]
    [InlineData("GovernmentOrganization")]
    [InlineData("SportsTeam")]
    [InlineData("Airline")]
    [InlineData("MusicGroup")]
    public void AppliesTo_OrganizationFamily_ReturnsTrue(string type)
    {
        _sut.AppliesTo(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("LocalBusiness")]
    [InlineData("Restaurant")]
    [InlineData("Person")]
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
        var json = FullyPopulated.Replace("\"name\": \"Example Co\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingUrl_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"url\": \"https://example.com\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.url");
    }

    [Fact]
    public void Check_MissingLogo_ProducesWarning()
    {
        var json = FullyPopulated.Replace(
            "\"logo\": { \"@type\": \"ImageObject\", \"url\": \"https://example.com/logo.png\" },",
            string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.logo");
    }

    [Fact]
    public void Check_MissingAddress_ProducesWarning()
    {
        var json = FullyPopulated.Replace(
            "\"address\": { \"@type\": \"PostalAddress\", \"streetAddress\": \"1 High St\", \"addressLocality\": \"London\" }",
            "\"dummy\": 0");
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.address");
    }

    [Fact]
    public void Check_PathPropagates()
    {
        var issues = _sut.Check(Parse(FullyPopulated.Replace("\"name\": \"Example Co\",", string.Empty)),
            "@graph[0]").ToList();

        issues.Should().ContainSingle(i => i.Path == "@graph[0].name");
    }
}
