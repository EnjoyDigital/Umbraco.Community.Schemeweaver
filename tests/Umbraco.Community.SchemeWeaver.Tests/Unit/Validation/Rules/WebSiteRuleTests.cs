using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class WebSiteRuleTests
{
    private readonly WebSiteRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "WebSite",
          "@id": "https://example.com/#website",
          "name": "Example",
          "url": "https://example.com",
          "publisher": { "@type": "Organization", "name": "Example Co" },
          "potentialAction": {
            "@type": "SearchAction",
            "target": {
              "@type": "EntryPoint",
              "urlTemplate": "https://example.com/search?q={search_term_string}"
            },
            "query-input": "required name=search_term_string"
          }
        }
        """;

    [Fact]
    public void AppliesTo_WebSite_ReturnsTrue()
    {
        _sut.AppliesTo("WebSite").Should().BeTrue();
    }

    [Theory]
    [InlineData("WebPage")]
    [InlineData("Article")]
    [InlineData("Organization")]
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
        var json = FullyPopulated.Replace("\"name\": \"Example\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingUrl_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"url\": \"https://example.com\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.url");
    }

    [Fact]
    public void Check_MissingPublisher_ProducesWarning()
    {
        var json = FullyPopulated.Replace(
            "\"publisher\": { \"@type\": \"Organization\", \"name\": \"Example Co\" },",
            string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.publisher");
    }

    [Fact]
    public void Check_MissingPotentialAction_ProducesWarning()
    {
        var json = """
            {
              "@type": "WebSite",
              "name": "Example",
              "url": "https://example.com",
              "publisher": { "@type": "Organization", "name": "Example Co" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.potentialAction");
    }
}
