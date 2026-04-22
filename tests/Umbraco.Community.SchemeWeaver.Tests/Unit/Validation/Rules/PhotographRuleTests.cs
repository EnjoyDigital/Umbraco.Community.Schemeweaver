using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class PhotographRuleTests
{
    private readonly PhotographRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Photograph",
          "@id": "https://example.com/photos/1#photograph",
          "name": "Sunset over the Thames",
          "contentUrl": "https://example.com/images/sunset.jpg",
          "author": { "@type": "Person", "name": "Jane Doe" },
          "datePublished": "2026-03-15",
          "license": "https://example.com/licence",
          "creditText": "Photo by Jane Doe"
        }
        """;

    [Fact]
    public void AppliesTo_Photograph_ReturnsTrue() => _sut.AppliesTo("Photograph").Should().BeTrue();

    [Theory]
    [InlineData("ImageObject")]
    [InlineData("Article")]
    [InlineData("MediaObject")]
    public void AppliesTo_OtherType_ReturnsFalse(string type) => _sut.AppliesTo(type).Should().BeFalse();

    [Fact]
    public void Check_FullyPopulated_ProducesNoIssues()
    {
        var issues = _sut.Check(Parse(FullyPopulated), "$").ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Check_AllChecksAreWarnings()
    {
        const string json = """
            {
              "@type": "Photograph"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotBeEmpty();
        issues.Should().OnlyContain(i => i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Check_MissingName_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"name\": \"Sunset over the Thames\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingContentUrl_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"contentUrl\": \"https://example.com/images/sunset.jpg\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.contentUrl");
    }

    [Fact]
    public void Check_MissingAuthor_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"author\": { \"@type\": \"Person\", \"name\": \"Jane Doe\" },", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.author");
    }

    [Fact]
    public void Check_MissingLicense_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"license\": \"https://example.com/licence\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.license");
    }

    [Fact]
    public void Check_MissingCreditText_ProducesWarning()
    {
        const string json = """
            {
              "@type": "Photograph",
              "name": "Sunset over the Thames",
              "contentUrl": "https://example.com/images/sunset.jpg",
              "author": { "@type": "Person", "name": "Jane Doe" },
              "datePublished": "2026-03-15",
              "license": "https://example.com/licence"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.creditText");
    }
}
