using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class EventRuleTests
{
    private readonly EventRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Event",
          "@id": "https://example.com/events/summit#event",
          "name": "Annual Summit",
          "description": "A gathering of experts.",
          "image": "https://example.com/images/summit.jpg",
          "startDate": "2026-06-15T20:00:00-05:00",
          "endDate": "2026-06-17T22:00:00-05:00",
          "location": { "@type": "Place", "name": "Conference Hall", "address": "123 Main St" },
          "offers": { "@type": "Offer", "price": "99.00", "priceCurrency": "USD", "url": "https://example.com/buy" },
          "performer": { "@type": "Person", "name": "Jane Doe" },
          "organizer": { "@type": "Organization", "name": "Acme Corp" }
        }
        """;

    [Theory]
    [InlineData("Event")]
    [InlineData("BusinessEvent")]
    [InlineData("MusicEvent")]
    [InlineData("Festival")]
    public void AppliesTo_EventFamily_ReturnsTrue(string type) => _sut.AppliesTo(type).Should().BeTrue();

    [Theory]
    [InlineData("Article")]
    [InlineData("Product")]
    public void AppliesTo_OtherType_ReturnsFalse(string type) => _sut.AppliesTo(type).Should().BeFalse();

    [Fact]
    public void Check_FullyPopulated_ProducesNoIssues()
    {
        _sut.Check(Parse(FullyPopulated), "$").Should().BeEmpty();
    }

    [Fact]
    public void Check_MissingName_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"name\": \"Annual Summit\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingStartDate_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"startDate\": \"2026-06-15T20:00:00-05:00\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.startDate");
    }

    [Fact]
    public void Check_MissingLocation_ProducesCritical()
    {
        var json = FullyPopulated.Replace(
            "\"location\": { \"@type\": \"Place\", \"name\": \"Conference Hall\", \"address\": \"123 Main St\" },",
            string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.location");
    }

    [Fact]
    public void Check_MissingEndDate_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"endDate\": \"2026-06-17T22:00:00-05:00\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Severity == ValidationSeverity.Warning && i.Path == "$.endDate");
    }

    [Fact]
    public void Check_MissingPerformerAndOrganizer_ProducesTwoWarnings()
    {
        const string json = """
            {
              "@type": "Event",
              "@id": "https://example.com/events/summit#event",
              "name": "Annual Summit",
              "description": "A gathering of experts.",
              "image": "https://example.com/images/summit.jpg",
              "startDate": "2026-06-15T20:00:00-05:00",
              "endDate": "2026-06-17T22:00:00-05:00",
              "location": { "@type": "Place", "name": "Conference Hall", "address": "123 Main St" },
              "offers": { "@type": "Offer", "price": "99.00", "priceCurrency": "USD", "url": "https://example.com/buy" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Path == "$.performer" && i.Severity == ValidationSeverity.Warning);
        issues.Should().Contain(i => i.Path == "$.organizer" && i.Severity == ValidationSeverity.Warning);
    }
}
