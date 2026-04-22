using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class AboutPageRuleTests
{
    private readonly AboutPageRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "AboutPage",
          "@id": "https://example.com/about#aboutpage",
          "name": "About Example Co",
          "url": "https://example.com/about",
          "description": "Example Co was founded in 2001 to test kitchens.",
          "image": "https://example.com/images/about.jpg",
          "isPartOf": { "@id": "https://example.com/#website" },
          "breadcrumb": { "@id": "https://example.com/about#breadcrumb" }
        }
        """;

    [Theory]
    [InlineData("AboutPage")]
    public void AppliesTo_AboutPage_ReturnsTrue(string type)
    {
        _sut.AppliesTo(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("WebPage")]
    [InlineData("Article")]
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
        var json = FullyPopulated.Replace("\"name\": \"About Example Co\",", string.Empty);
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
        var json = FullyPopulated.Replace("\"description\": \"Example Co was founded in 2001 to test kitchens.\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.description");
    }

    [Fact]
    public void Check_MissingIsPartOf_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"isPartOf\": { \"@id\": \"https://example.com/#website\" },", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.isPartOf");
    }
}
