using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class ArticleRuleTests
{
    private readonly ArticleRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Article",
          "@id": "https://example.com/posts/hello#article",
          "headline": "Hello World",
          "image": "https://example.com/images/hello.jpg",
          "datePublished": "2026-04-01T10:00:00Z",
          "dateModified": "2026-04-02T11:00:00Z",
          "author": { "@type": "Person", "name": "Jane Doe" },
          "publisher": { "@type": "Organization", "name": "Example Co" },
          "description": "A first post."
        }
        """;

    [Theory]
    [InlineData("Article")]
    [InlineData("BlogPosting")]
    [InlineData("NewsArticle")]
    [InlineData("TechArticle")]
    public void AppliesTo_ArticleFamily_ReturnsTrue(string type)
    {
        _sut.AppliesTo(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("Product")]
    [InlineData("Event")]
    [InlineData("Person")]
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
    public void Check_MissingHeadline_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"headline\": \"Hello World\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.headline");
    }

    [Fact]
    public void Check_MissingImage_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"image\": \"https://example.com/images/hello.jpg\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.image");
    }

    [Fact]
    public void Check_MissingDatePublished_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"datePublished\": \"2026-04-01T10:00:00Z\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.datePublished");
    }

    [Fact]
    public void Check_MissingAuthor_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"author\": { \"@type\": \"Person\", \"name\": \"Jane Doe\" },", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.author");
    }

    [Fact]
    public void Check_MissingDateModified_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"dateModified\": \"2026-04-02T11:00:00Z\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.dateModified");
    }

    [Fact]
    public void Check_MissingPublisher_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"publisher\": { \"@type\": \"Organization\", \"name\": \"Example Co\" },", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.publisher");
    }

    [Fact]
    public void Check_NonIsoDatePublished_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"datePublished\": \"2026-04-01T10:00:00Z\"", "\"datePublished\": \"not a date\"");
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.datePublished");
    }

    [Fact]
    public void Check_PathPropagates()
    {
        var issues = _sut.Check(Parse(FullyPopulated.Replace("\"headline\": \"Hello World\",", string.Empty)),
            "@graph[2]").ToList();

        issues.Should().ContainSingle(i => i.Path == "@graph[2].headline");
    }
}
