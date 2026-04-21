using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class WebPageRuleTests
{
    private readonly WebPageRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "WebPage",
          "@id": "https://example.com/about#webpage",
          "name": "About the Test Kitchen",
          "url": "https://example.com/about",
          "description": "Everything you wanted to know about the kitchen.",
          "image": "https://example.com/images/about.jpg",
          "breadcrumb": { "@id": "https://example.com/about#breadcrumb" },
          "isPartOf": { "@id": "https://example.com/#website" }
        }
        """;

    [Theory]
    [InlineData("WebPage")]
    [InlineData("CollectionPage")]
    [InlineData("ContactPage")]
    public void AppliesTo_WebPageFamily_ReturnsTrue(string type)
    {
        _sut.AppliesTo(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("Article")]
    [InlineData("AboutPage")]
    [InlineData("ProfilePage")]
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
        var json = FullyPopulated.Replace("\"name\": \"About the Test Kitchen\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingUrl_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"url\": \"https://example.com/about\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.url");
    }

    [Fact]
    public void Check_MissingDescription_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"description\": \"Everything you wanted to know about the kitchen.\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.description");
    }

    [Fact]
    public void Check_MissingBreadcrumb_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"breadcrumb\": { \"@id\": \"https://example.com/about#breadcrumb\" },", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.breadcrumb");
    }
}
