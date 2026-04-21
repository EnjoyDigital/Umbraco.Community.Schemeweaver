using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class HowToRuleTests
{
    private readonly HowToRule _sut = new();

    private const string FullyPopulatedJson = """
        {
          "@type": "HowTo",
          "name": "How to tie a bowline knot",
          "image": "https://example.com/images/bowline.jpg",
          "totalTime": "PT5M",
          "estimatedCost": { "@type": "MonetaryAmount", "currency": "USD", "value": "0" },
          "supply": [ { "@type": "HowToSupply", "name": "Rope" } ],
          "tool": [ { "@type": "HowToTool", "name": "None" } ],
          "step": [
            { "@type": "HowToStep", "name": "Form a loop", "text": "Make a loop near the end of the rope." },
            { "@type": "HowToStep", "name": "Pass the rabbit", "text": "Pass the working end through the loop." }
          ]
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
        const string json = """
            {
              "@type": "HowTo",
              "step": [ { "@type": "HowToStep", "text": "Do the thing." } ]
            }
            """;

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingStep_YieldsCritical()
    {
        const string json = """
            {
              "@type": "HowTo",
              "name": "Do something"
            }
            """;

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.step");
    }

    [Fact]
    public void Check_StepWithoutNameOrText_YieldsWarningForThatStep()
    {
        const string json = """
            {
              "@type": "HowTo",
              "name": "Do a thing",
              "step": [
                { "@type": "HowToStep", "name": "First", "text": "Do a thing." },
                { "@type": "HowToStep" }
              ]
            }
            """;

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Path == "$.step[1]" && i.Severity == ValidationSeverity.Warning);
        issues.Should().NotContain(i => i.Path == "$.step[0]");
    }

    [Fact]
    public void Check_MissingTotalTime_YieldsWarning()
    {
        var json = FullyPopulatedJson.Replace("\"totalTime\": \"PT5M\",", string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.totalTime")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void AppliesTo_Article_IsFalse()
    {
        _sut.AppliesTo("Article").Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_HowTo_IsTrue()
    {
        _sut.AppliesTo("HowTo").Should().BeTrue();
    }
}
