using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class VideoObjectRuleTests
{
    private readonly VideoObjectRule _sut = new();

    private const string FullyPopulatedJson = """
        {
          "@type": "VideoObject",
          "name": "Building an Umbraco package",
          "description": "Walkthrough of creating a community package for Umbraco CMS.",
          "thumbnailUrl": "https://example.com/thumbs/pkg.jpg",
          "uploadDate": "2026-03-15T10:00:00Z",
          "duration": "PT8M20S",
          "contentUrl": "https://example.com/videos/pkg.mp4",
          "embedUrl": "https://example.com/embed/pkg",
          "interactionStatistic": {
            "@type": "InteractionCounter",
            "interactionType": { "@type": "WatchAction" },
            "userInteractionCount": 5000
          }
        }
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Check_FullyPopulated_YieldsNoIssues()
    {
        var issues = _sut.Check(Parse(FullyPopulatedJson), "$").ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Check_MissingName_YieldsCritical()
    {
        var json = FullyPopulatedJson.Replace("\"name\": \"Building an Umbraco package\",", string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingDescription_YieldsCritical()
    {
        var json = FullyPopulatedJson.Replace(
            "\"description\": \"Walkthrough of creating a community package for Umbraco CMS.\",",
            string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.description");
    }

    [Fact]
    public void Check_MissingThumbnailUrl_YieldsCritical()
    {
        var json = FullyPopulatedJson.Replace(
            "\"thumbnailUrl\": \"https://example.com/thumbs/pkg.jpg\",",
            string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.thumbnailUrl");
    }

    [Fact]
    public void Check_MissingUploadDate_YieldsCritical()
    {
        var json = FullyPopulatedJson.Replace(
            "\"uploadDate\": \"2026-03-15T10:00:00Z\",",
            string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.uploadDate");
    }

    [Fact]
    public void Check_MissingDuration_YieldsWarning()
    {
        var json = FullyPopulatedJson.Replace("\"duration\": \"PT8M20S\",", string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.duration")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void Check_MissingInteractionStatistic_YieldsWarning()
    {
        const string json = """
            {
              "@type": "VideoObject",
              "name": "A video",
              "description": "Text.",
              "thumbnailUrl": "https://example.com/t.jpg",
              "uploadDate": "2026-03-15T10:00:00Z",
              "duration": "PT1M",
              "contentUrl": "https://example.com/v.mp4",
              "embedUrl": "https://example.com/e"
            }
            """;

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.interactionStatistic")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void AppliesTo_Article_IsFalse()
    {
        _sut.AppliesTo("Article").Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_Movie_IsFalse()
    {
        // Movie should be handled by MovieRule, not VideoObjectRule.
        _sut.AppliesTo("Movie").Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_VideoObject_IsTrue()
    {
        _sut.AppliesTo("VideoObject").Should().BeTrue();
    }
}
