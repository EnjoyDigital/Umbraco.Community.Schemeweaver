using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class OccupationRuleTests
{
    private readonly OccupationRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "Occupation",
          "@id": "https://example.com/careers/engineer#occupation",
          "name": "Software Engineer",
          "description": "Builds and maintains cloud-native services.",
          "occupationLocation": { "@type": "City", "name": "London" },
          "estimatedSalary": {
            "@type": "MonetaryAmountDistribution",
            "name": "base",
            "currency": "GBP",
            "duration": "P1Y",
            "median": 65000
          },
          "qualifications": "Bachelor's degree in computer science or equivalent experience.",
          "skills": "C#, ASP.NET Core, Azure, distributed systems"
        }
        """;

    [Theory]
    [InlineData("Occupation")]
    public void AppliesTo_Occupation_ReturnsTrue(string type)
    {
        _sut.AppliesTo(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("JobPosting")]
    [InlineData("Person")]
    [InlineData("EducationalOccupationalProgram")]
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
    public void Check_AllIssuesAreWarnings()
    {
        var json = """
            {
              "@type": "Occupation",
              "@id": "https://example.com/careers/engineer#occupation"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotBeEmpty();
        issues.Should().OnlyContain(i => i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Check_MissingName_ProducesWarning()
    {
        var json = FullyPopulated.Replace("\"name\": \"Software Engineer\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingEstimatedSalary_ProducesWarning()
    {
        var json = """
            {
              "@type": "Occupation",
              "@id": "https://example.com/careers/engineer#occupation",
              "name": "Software Engineer",
              "description": "Builds and maintains cloud-native services.",
              "occupationLocation": { "@type": "City", "name": "London" },
              "qualifications": "Bachelor's degree in computer science or equivalent experience.",
              "skills": "C#, ASP.NET Core, Azure, distributed systems"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.estimatedSalary");
    }
}
