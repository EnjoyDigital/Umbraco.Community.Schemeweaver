using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class MovieRuleTests
{
    private readonly MovieRule _sut = new();

    private const string FullyPopulatedJson = """
        {
          "@type": "Movie",
          "name": "The Grand Umbraco Heist",
          "image": "https://example.com/posters/heist.jpg",
          "url": "https://example.com/movies/heist",
          "director": { "@type": "Person", "name": "A. Director" },
          "dateCreated": "2026-02-15",
          "aggregateRating": { "@type": "AggregateRating", "ratingValue": "4.5", "reviewCount": "1234" },
          "review": [ { "@type": "Review", "reviewBody": "Great fun." } ],
          "genre": ["Comedy", "Heist"]
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
        var json = FullyPopulatedJson.Replace("\"name\": \"The Grand Umbraco Heist\",", string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingImage_YieldsCritical()
    {
        var json = FullyPopulatedJson.Replace("\"image\": \"https://example.com/posters/heist.jpg\",", string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.image");
    }

    [Fact]
    public void Check_MissingDirector_YieldsWarning()
    {
        var json = FullyPopulatedJson.Replace(
            "\"director\": { \"@type\": \"Person\", \"name\": \"A. Director\" },",
            string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.director")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void Check_MissingGenre_YieldsWarning()
    {
        var json = FullyPopulatedJson.Replace(
            ",\n  \"genre\": [\"Comedy\", \"Heist\"]",
            string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.genre")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void AppliesTo_Article_IsFalse()
    {
        _sut.AppliesTo("Article").Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_Movie_IsTrue()
    {
        _sut.AppliesTo("Movie").Should().BeTrue();
    }
}
