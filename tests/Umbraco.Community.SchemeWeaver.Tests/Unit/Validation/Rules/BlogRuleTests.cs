using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class BlogRuleTests
{
    private readonly BlogRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulatedBlog = """
        {
          "@type": "Blog",
          "@id": "https://example.com/blog#blog",
          "name": "The Example Blog",
          "description": "Notes from the Example team.",
          "url": "https://example.com/blog",
          "author": { "@type": "Person", "name": "Jane Doe" },
          "publisher": { "@type": "Organization", "name": "Example Co" }
        }
        """;

    private const string FullyPopulatedLiveBlog = """
        {
          "@type": "LiveBlogPosting",
          "@id": "https://example.com/live/event-2026#liveblog",
          "name": "Live: The Example Event 2026",
          "description": "Live coverage of the 2026 event.",
          "url": "https://example.com/live/event-2026",
          "author": { "@type": "Person", "name": "Jane Doe" },
          "publisher": { "@type": "Organization", "name": "Example Co" },
          "coverageStartTime": "2026-04-15T09:00:00Z",
          "coverageEndTime": "2026-04-15T18:00:00Z",
          "liveBlogUpdate": [
            {
              "@type": "BlogPosting",
              "headline": "Doors open",
              "datePublished": "2026-04-15T09:00:00Z",
              "articleBody": "The event hall is now open."
            }
          ]
        }
        """;

    [Theory]
    [InlineData("Blog")]
    [InlineData("LiveBlogPosting")]
    public void AppliesTo_BlogFamily_ReturnsTrue(string type)
    {
        _sut.AppliesTo(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("BlogPosting")]
    [InlineData("Article")]
    [InlineData("WebPage")]
    public void AppliesTo_OtherType_ReturnsFalse(string type)
    {
        _sut.AppliesTo(type).Should().BeFalse();
    }

    [Fact]
    public void Check_FullyPopulatedBlog_ProducesNoIssues()
    {
        var issues = _sut.Check(Parse(FullyPopulatedBlog), "$").ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Check_FullyPopulatedLiveBlog_ProducesNoIssues()
    {
        var issues = _sut.Check(Parse(FullyPopulatedLiveBlog), "$").ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Check_Blog_MissingName_ProducesCritical()
    {
        var json = FullyPopulatedBlog.Replace("\"name\": \"The Example Blog\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_Blog_MissingDescription_ProducesWarning()
    {
        var json = FullyPopulatedBlog.Replace("\"description\": \"Notes from the Example team.\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.description");
    }

    [Fact]
    public void Check_Blog_DoesNotEmitLiveBlogFields()
    {
        // A plain Blog should never get live-coverage warnings.
        var issues = _sut.Check(Parse(FullyPopulatedBlog), "$").ToList();

        issues.Should().NotContain(i => i.Path.EndsWith("coverageStartTime"));
        issues.Should().NotContain(i => i.Path.EndsWith("coverageEndTime"));
        issues.Should().NotContain(i => i.Path.EndsWith("liveBlogUpdate"));
    }

    [Fact]
    public void Check_LiveBlog_MissingCoverageStartTime_ProducesWarning()
    {
        var json = FullyPopulatedLiveBlog.Replace("\"coverageStartTime\": \"2026-04-15T09:00:00Z\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.coverageStartTime");
    }

    [Fact]
    public void Check_LiveBlog_MissingLiveBlogUpdate_ProducesWarning()
    {
        var json = """
            {
              "@type": "LiveBlogPosting",
              "@id": "https://example.com/live/event-2026#liveblog",
              "name": "Live: The Example Event 2026",
              "description": "Live coverage of the 2026 event.",
              "url": "https://example.com/live/event-2026",
              "author": { "@type": "Person", "name": "Jane Doe" },
              "publisher": { "@type": "Organization", "name": "Example Co" },
              "coverageStartTime": "2026-04-15T09:00:00Z",
              "coverageEndTime": "2026-04-15T18:00:00Z"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.liveBlogUpdate");
    }
}
