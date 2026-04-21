using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class ProfilePageRuleTests
{
    private readonly ProfilePageRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "ProfilePage",
          "@id": "https://example.com/authors/jane#profile",
          "name": "Jane Doe",
          "url": "https://example.com/authors/jane",
          "dateCreated": "2022-01-01T00:00:00Z",
          "dateModified": "2026-04-15T00:00:00Z",
          "mainEntity": {
            "@type": "Person",
            "name": "Jane Doe",
            "url": "https://example.com/authors/jane"
          }
        }
        """;

    [Theory]
    [InlineData("ProfilePage")]
    public void AppliesTo_ProfilePage_ReturnsTrue(string type)
    {
        _sut.AppliesTo(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("Article")]
    [InlineData("WebPage")]
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
    public void Check_MissingMainEntity_ProducesCritical()
    {
        var json = """
            {
              "@type": "ProfilePage",
              "@id": "https://example.com/authors/jane#profile",
              "name": "Jane Doe",
              "url": "https://example.com/authors/jane",
              "dateCreated": "2022-01-01T00:00:00Z",
              "dateModified": "2026-04-15T00:00:00Z"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.mainEntity");
    }

    [Fact]
    public void Check_MainEntityWithoutName_ProducesCritical()
    {
        var json = """
            {
              "@type": "ProfilePage",
              "@id": "https://example.com/authors/jane#profile",
              "name": "Jane Doe",
              "url": "https://example.com/authors/jane",
              "dateCreated": "2022-01-01T00:00:00Z",
              "dateModified": "2026-04-15T00:00:00Z",
              "mainEntity": { "@type": "Person", "url": "https://example.com/authors/jane" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.mainEntity");
    }

    [Fact]
    public void Check_MissingDateCreated_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"dateCreated\": \"2022-01-01T00:00:00Z\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.dateCreated");
    }

    [Fact]
    public void Check_MissingDateModified_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"dateModified\": \"2026-04-15T00:00:00Z\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.dateModified");
    }
}
