using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class JobPostingRuleTests
{
    private readonly JobPostingRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "JobPosting",
          "title": "Senior Widget Engineer",
          "description": "<p>Come build widgets.</p>",
          "datePosted": "2026-04-01",
          "validThrough": "2026-06-01T00:00:00Z",
          "hiringOrganization": {
            "@type": "Organization",
            "name": "Example Co",
            "sameAs": "https://example.com"
          },
          "jobLocation": {
            "@type": "Place",
            "address": {
              "@type": "PostalAddress",
              "streetAddress": "1 High St",
              "addressLocality": "London",
              "postalCode": "SW1A 1AA",
              "addressCountry": "GB"
            }
          },
          "baseSalary": {
            "@type": "MonetaryAmount",
            "currency": "GBP",
            "value": { "@type": "QuantitativeValue", "minValue": 60000, "maxValue": 80000, "unitText": "YEAR" }
          },
          "employmentType": "FULL_TIME",
          "identifier": { "@type": "PropertyValue", "name": "Example Co", "value": "JOB-123" }
        }
        """;

    [Fact]
    public void AppliesTo_JobPosting_ReturnsTrue()
    {
        _sut.AppliesTo("JobPosting").Should().BeTrue();
    }

    [Theory]
    [InlineData("Article")]
    [InlineData("Organization")]
    [InlineData("Event")]
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
    public void Check_MissingTitle_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"title\": \"Senior Widget Engineer\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.title");
    }

    [Fact]
    public void Check_MissingDescription_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"description\": \"<p>Come build widgets.</p>\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.description");
    }

    [Fact]
    public void Check_MissingDatePosted_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"datePosted\": \"2026-04-01\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.datePosted");
    }

    [Fact]
    public void Check_MissingValidThrough_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"validThrough\": \"2026-06-01T00:00:00Z\",", string.Empty);
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.validThrough");
    }

    [Fact]
    public void Check_MissingHiringOrganization_ProducesCritical()
    {
        var json = """
            {
              "@type": "JobPosting",
              "title": "Senior Widget Engineer",
              "description": "<p>Come build widgets.</p>",
              "datePosted": "2026-04-01",
              "validThrough": "2026-06-01T00:00:00Z",
              "jobLocation": {
                "@type": "Place",
                "address": { "@type": "PostalAddress", "addressLocality": "London" }
              }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.hiringOrganization");
    }

    [Fact]
    public void Check_HiringOrganizationWithoutName_ProducesCritical()
    {
        var json = FullyPopulated.Replace(
            "\"hiringOrganization\": {\n    \"@type\": \"Organization\",\n    \"name\": \"Example Co\",\n    \"sameAs\": \"https://example.com\"\n  },",
            "\"hiringOrganization\": { \"@type\": \"Organization\" },");
        // Replace may not have matched due to whitespace; do a tolerant swap instead.
        var withBrokenHiringOrg = """
            {
              "@type": "JobPosting",
              "title": "Senior Widget Engineer",
              "description": "<p>Come build widgets.</p>",
              "datePosted": "2026-04-01",
              "validThrough": "2026-06-01T00:00:00Z",
              "hiringOrganization": { "@type": "Organization" },
              "jobLocation": {
                "@type": "Place",
                "address": { "@type": "PostalAddress", "addressLocality": "London" }
              }
            }
            """;
        var issues = _sut.Check(Parse(withBrokenHiringOrg), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.hiringOrganization");
    }

    [Fact]
    public void Check_MissingJobLocation_ProducesCritical()
    {
        var json = """
            {
              "@type": "JobPosting",
              "title": "Senior Widget Engineer",
              "description": "<p>Come build widgets.</p>",
              "datePosted": "2026-04-01",
              "validThrough": "2026-06-01T00:00:00Z",
              "hiringOrganization": { "@type": "Organization", "name": "Example Co" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.jobLocation");
    }

    [Fact]
    public void Check_RemoteRoleWithApplicantLocationRequirementsAndTelecommute_IsAccepted()
    {
        var json = """
            {
              "@type": "JobPosting",
              "title": "Senior Widget Engineer",
              "description": "<p>Come build widgets, remotely.</p>",
              "datePosted": "2026-04-01",
              "validThrough": "2026-06-01T00:00:00Z",
              "hiringOrganization": { "@type": "Organization", "name": "Example Co" },
              "jobLocationType": "TELECOMMUTE",
              "applicantLocationRequirements": { "@type": "Country", "name": "UK" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotContain(i => i.Path == "$.jobLocation");
    }

    [Fact]
    public void Check_RemoteFlagWithoutApplicantLocationRequirements_StillProducesCritical()
    {
        var json = """
            {
              "@type": "JobPosting",
              "title": "Senior Widget Engineer",
              "description": "<p>Come build widgets, remotely.</p>",
              "datePosted": "2026-04-01",
              "validThrough": "2026-06-01T00:00:00Z",
              "hiringOrganization": { "@type": "Organization", "name": "Example Co" },
              "jobLocationType": "TELECOMMUTE"
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.jobLocation");
    }

    [Fact]
    public void Check_MissingBaseSalary_ProducesWarning()
    {
        var json = """
            {
              "@type": "JobPosting",
              "title": "Senior Widget Engineer",
              "description": "<p>Come build widgets.</p>",
              "datePosted": "2026-04-01",
              "validThrough": "2026-06-01T00:00:00Z",
              "hiringOrganization": { "@type": "Organization", "name": "Example Co" },
              "jobLocation": { "@type": "Place", "address": { "@type": "PostalAddress", "addressLocality": "London" } },
              "employmentType": "FULL_TIME",
              "identifier": { "@type": "PropertyValue", "value": "JOB-123" }
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Warning
            && i.Path == "$.baseSalary");
    }

    [Fact]
    public void Check_NonIsoDatePosted_ProducesCritical()
    {
        var json = FullyPopulated.Replace("\"datePosted\": \"2026-04-01\"", "\"datePosted\": \"not a date\"");
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.datePosted");
    }
}
