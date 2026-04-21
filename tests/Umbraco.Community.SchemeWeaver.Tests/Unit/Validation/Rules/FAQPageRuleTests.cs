using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class FAQPageRuleTests
{
    private readonly FAQPageRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "FAQPage",
          "mainEntity": [
            {
              "@type": "Question",
              "name": "What is SchemeWeaver?",
              "acceptedAnswer": { "@type": "Answer", "text": "An Umbraco package." }
            },
            {
              "@type": "Question",
              "name": "Is it free?",
              "acceptedAnswer": { "@type": "Answer", "text": "Yes." }
            }
          ]
        }
        """;

    [Fact]
    public void AppliesTo_FAQPage_ReturnsTrue()
    {
        _sut.AppliesTo("FAQPage").Should().BeTrue();
    }

    [Theory]
    [InlineData("WebPage")]
    [InlineData("QAPage")]
    [InlineData("Article")]
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
        var json = """{ "@type": "FAQPage" }""";
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.mainEntity");
    }

    [Fact]
    public void Check_EmptyMainEntity_ProducesCritical()
    {
        var json = """{ "@type": "FAQPage", "mainEntity": [] }""";
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.mainEntity");
    }

    [Fact]
    public void Check_QuestionWithoutName_ProducesCritical()
    {
        var json = """
            {
              "@type": "FAQPage",
              "mainEntity": [
                {
                  "@type": "Question",
                  "acceptedAnswer": { "@type": "Answer", "text": "An Umbraco package." }
                }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.mainEntity[0].name");
    }

    [Fact]
    public void Check_QuestionWithoutAcceptedAnswer_ProducesCritical()
    {
        var json = """
            {
              "@type": "FAQPage",
              "mainEntity": [
                { "@type": "Question", "name": "What is SchemeWeaver?" }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.mainEntity[0].acceptedAnswer");
    }

    [Fact]
    public void Check_AcceptedAnswerWithoutText_ProducesCritical()
    {
        var json = """
            {
              "@type": "FAQPage",
              "mainEntity": [
                {
                  "@type": "Question",
                  "name": "What is SchemeWeaver?",
                  "acceptedAnswer": { "@type": "Answer" }
                }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.mainEntity[0].acceptedAnswer");
    }

    [Fact]
    public void Check_OnlyOneBadQuestion_FlagsByIndex()
    {
        var json = """
            {
              "@type": "FAQPage",
              "mainEntity": [
                {
                  "@type": "Question",
                  "name": "Good question?",
                  "acceptedAnswer": { "@type": "Answer", "text": "Good answer." }
                },
                {
                  "@type": "Question",
                  "acceptedAnswer": { "@type": "Answer", "text": "No name." }
                }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.mainEntity[1].name");
        issues.Should().NotContain(i => i.Path == "$.mainEntity[0].name");
    }
}
