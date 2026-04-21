using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class BreadcrumbListRuleTests
{
    private readonly BreadcrumbListRule _sut = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string FullyPopulated = """
        {
          "@type": "BreadcrumbList",
          "itemListElement": [
            {
              "@type": "ListItem",
              "position": 1,
              "name": "Home",
              "item": "https://example.com/"
            },
            {
              "@type": "ListItem",
              "position": 2,
              "name": "Blog",
              "item": { "@type": "WebPage", "@id": "https://example.com/blog/" }
            },
            {
              "@type": "ListItem",
              "position": 3,
              "name": "Post",
              "item": { "@type": "WebPage", "url": "https://example.com/blog/post/" }
            }
          ]
        }
        """;

    [Fact]
    public void AppliesTo_BreadcrumbList_ReturnsTrue()
    {
        _sut.AppliesTo("BreadcrumbList").Should().BeTrue();
    }

    [Theory]
    [InlineData("ItemList")]
    [InlineData("Article")]
    [InlineData("WebSite")]
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
    public void Check_MissingItemListElement_ProducesCritical()
    {
        var json = """{ "@type": "BreadcrumbList" }""";
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.itemListElement");
    }

    [Fact]
    public void Check_EmptyItemListElement_ProducesCritical()
    {
        var json = """{ "@type": "BreadcrumbList", "itemListElement": [] }""";
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.itemListElement");
    }

    [Fact]
    public void Check_ListItemMissingPosition_ProducesCritical()
    {
        var json = """
            {
              "@type": "BreadcrumbList",
              "itemListElement": [
                { "@type": "ListItem", "name": "Home", "item": "https://example.com/" }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.itemListElement[0].position");
    }

    [Fact]
    public void Check_ListItemMissingName_ProducesCritical()
    {
        var json = """
            {
              "@type": "BreadcrumbList",
              "itemListElement": [
                { "@type": "ListItem", "position": 1, "item": "https://example.com/" }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.itemListElement[0].name");
    }

    [Fact]
    public void Check_ListItemMissingItem_ProducesCritical()
    {
        var json = """
            {
              "@type": "BreadcrumbList",
              "itemListElement": [
                { "@type": "ListItem", "position": 1, "name": "Home" }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.itemListElement[0].item");
    }

    [Fact]
    public void Check_ListItemWithItemAsIdObject_IsAccepted()
    {
        var json = """
            {
              "@type": "BreadcrumbList",
              "itemListElement": [
                {
                  "@type": "ListItem",
                  "position": 1,
                  "name": "Home",
                  "item": { "@type": "WebPage", "@id": "https://example.com/" }
                }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotContain(i => i.Path.EndsWith(".item"));
    }

    [Fact]
    public void Check_ListItemWithItemAsNonUrlString_ProducesCritical()
    {
        var json = """
            {
              "@type": "BreadcrumbList",
              "itemListElement": [
                { "@type": "ListItem", "position": 1, "name": "Home", "item": "not-a-url" }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i =>
            i.Severity == ValidationSeverity.Critical
            && i.Path == "$.itemListElement[0].item");
    }

    [Fact]
    public void Check_SecondItemInvalidOnlyFlagsThatIndex()
    {
        var json = """
            {
              "@type": "BreadcrumbList",
              "itemListElement": [
                { "@type": "ListItem", "position": 1, "name": "Home", "item": "https://example.com/" },
                { "@type": "ListItem", "position": 2, "item": "https://example.com/blog/" }
              ]
            }
            """;
        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.itemListElement[1].name");
        issues.Should().NotContain(i => i.Path == "$.itemListElement[0].name");
    }
}
