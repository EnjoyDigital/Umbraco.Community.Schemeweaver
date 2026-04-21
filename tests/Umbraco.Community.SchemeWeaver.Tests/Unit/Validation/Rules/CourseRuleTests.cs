using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class CourseRuleTests
{
    private readonly CourseRule _sut = new();

    private const string FullyPopulatedJson = """
        {
          "@type": "Course",
          "name": "Introduction to Umbraco",
          "description": "Learn to build sites with Umbraco CMS.",
          "provider": { "@type": "Organization", "name": "Umbraco HQ", "url": "https://umbraco.com" },
          "offers": [ { "@type": "Offer", "price": "0", "priceCurrency": "EUR", "category": "Free" } ],
          "hasCourseInstance": [
            { "@type": "CourseInstance", "courseMode": "online", "startDate": "2026-05-01", "endDate": "2026-05-14" }
          ],
          "aggregateRating": { "@type": "AggregateRating", "ratingValue": "4.7", "reviewCount": "42" },
          "url": "https://example.com/courses/intro-to-umbraco"
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
        var json = FullyPopulatedJson.Replace("\"name\": \"Introduction to Umbraco\",", string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingDescription_YieldsCritical()
    {
        var json = FullyPopulatedJson.Replace("\"description\": \"Learn to build sites with Umbraco CMS.\",", string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.description");
    }

    [Fact]
    public void Check_MissingProvider_YieldsCritical()
    {
        const string json = """
            {
              "@type": "Course",
              "name": "A Course",
              "description": "Something."
            }
            """;

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.provider");
    }

    [Fact]
    public void Check_ProviderWithoutName_YieldsCriticalOnProviderName()
    {
        const string json = """
            {
              "@type": "Course",
              "name": "A Course",
              "description": "Something.",
              "provider": { "@type": "Organization", "url": "https://example.com" }
            }
            """;

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.provider.name");
    }

    [Fact]
    public void Check_MissingOffers_YieldsWarning()
    {
        var json = FullyPopulatedJson.Replace(
            "\"offers\": [ { \"@type\": \"Offer\", \"price\": \"0\", \"priceCurrency\": \"EUR\", \"category\": \"Free\" } ],",
            string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.offers")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void Check_MissingUrl_YieldsWarning()
    {
        var json = FullyPopulatedJson.Replace(
            ",\n  \"url\": \"https://example.com/courses/intro-to-umbraco\"",
            string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.url")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void AppliesTo_Article_IsFalse()
    {
        _sut.AppliesTo("Article").Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_CourseAndCourseInstance_AreTrue()
    {
        _sut.AppliesTo("Course").Should().BeTrue();
        _sut.AppliesTo("CourseInstance").Should().BeTrue();
    }
}
