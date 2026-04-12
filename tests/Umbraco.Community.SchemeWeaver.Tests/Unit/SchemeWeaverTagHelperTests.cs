using FluentAssertions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Xunit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.TagHelpers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class SchemeWeaverTagHelperTests
{
    private readonly IJsonLdGenerator _generator = Substitute.For<IJsonLdGenerator>();
    private readonly IVariationContextAccessor _variationContextAccessor = Substitute.For<IVariationContextAccessor>();
    private readonly ILogger<SchemeWeaverTagHelper> _logger = Substitute.For<ILogger<SchemeWeaverTagHelper>>();

    private SchemeWeaverTagHelper CreateTagHelper()
    {
        return new SchemeWeaverTagHelper(_generator, _variationContextAccessor, _logger);
    }

    private static (TagHelperContext context, TagHelperOutput output) CreateTagHelperContextAndOutput()
    {
        var context = new TagHelperContext(
            tagName: "scheme-weaver",
            allAttributes: new TagHelperAttributeList(),
            items: new Dictionary<object, object>(),
            uniqueId: Guid.NewGuid().ToString());

        var output = new TagHelperOutput(
            tagName: "scheme-weaver",
            attributes: new TagHelperAttributeList(),
            getChildContentAsync: (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        return (context, output);
    }

    [Fact]
    public void Process_WithNullContent_SuppressesOutput()
    {
        var helper = CreateTagHelper();
        helper.Content = null;
        var (context, output) = CreateTagHelperContextAndOutput();

        helper.Process(context, output);

        output.TagName.Should().BeNull();
        var html = GetContentHtml(output);
        html.Should().BeEmpty();
    }

    [Fact]
    public void Process_WithNoMapping_SuppressesOutput()
    {
        var content = Substitute.For<IPublishedContent>();
        _generator.GenerateJsonLdString(content).Returns((string?)null);
        _generator.GenerateInheritedJsonLdStrings(content).Returns([]);
        _generator.GenerateBlockElementJsonLdStrings(content).Returns([]);

        var helper = CreateTagHelper();
        helper.Content = content;
        var (context, output) = CreateTagHelperContextAndOutput();

        helper.Process(context, output);

        output.TagName.Should().BeNull();
        var html = GetContentHtml(output);
        html.Should().BeEmpty();
    }

    [Fact]
    public void Process_WithJsonLd_OutputsScriptTag()
    {
        var content = Substitute.For<IPublishedContent>();
        var jsonLd = """{"@type":"Article","headline":"Test"}""";
        _generator.GenerateJsonLdString(content).Returns(jsonLd);
        _generator.GenerateInheritedJsonLdStrings(content).Returns([]);
        _generator.GenerateBlockElementJsonLdStrings(content).Returns([]);

        var helper = CreateTagHelper();
        helper.Content = content;
        var (context, output) = CreateTagHelperContextAndOutput();

        helper.Process(context, output);

        var html = GetContentHtml(output);
        html.Should().Contain("<script type=\"application/ld+json\">");
        html.Should().Contain(jsonLd);
        html.Should().Contain("</script>");
    }

    [Fact]
    public void Process_WithBreadcrumb_OutputsSeparateScriptTags()
    {
        var content = Substitute.For<IPublishedContent>();
        var mainJson = """{"@type":"Article"}""";
        var breadcrumbJson = """{"@type":"BreadcrumbList"}""";

        _generator.GenerateJsonLdString(content).Returns(mainJson);
        _generator.GenerateBreadcrumbJsonLd(content).Returns(breadcrumbJson);
        _generator.GenerateInheritedJsonLdStrings(content).Returns([]);
        _generator.GenerateBlockElementJsonLdStrings(content).Returns([]);

        var helper = CreateTagHelper();
        helper.Content = content;
        var (context, output) = CreateTagHelperContextAndOutput();

        helper.Process(context, output);

        var html = GetContentHtml(output);
        // Count script tags — should be exactly 2, not nested
        var scriptOpenCount = CountOccurrences(html, "<script type=\"application/ld+json\">");
        var scriptCloseCount = CountOccurrences(html, "</script>");

        scriptOpenCount.Should().Be(2);
        scriptCloseCount.Should().Be(2);
        html.Should().Contain(mainJson);
        html.Should().Contain(breadcrumbJson);
    }

    [Fact]
    public void Process_WithInheritedSchemas_OutputsSeparateNonNestedScriptTags()
    {
        var content = Substitute.For<IPublishedContent>();
        var mainJson = """{"@type":"Event"}""";
        var breadcrumbJson = """{"@type":"BreadcrumbList"}""";
        var inheritedJson = """{"@type":"WebSite","name":"My Site"}""";

        _generator.GenerateJsonLdString(content).Returns(mainJson);
        _generator.GenerateBreadcrumbJsonLd(content).Returns(breadcrumbJson);
        _generator.GenerateInheritedJsonLdStrings(content).Returns([inheritedJson]);
        _generator.GenerateBlockElementJsonLdStrings(content).Returns([]);

        var helper = CreateTagHelper();
        helper.Content = content;
        var (context, output) = CreateTagHelperContextAndOutput();

        helper.Process(context, output);

        var html = GetContentHtml(output);
        var scriptOpenCount = CountOccurrences(html, "<script type=\"application/ld+json\">");
        var scriptCloseCount = CountOccurrences(html, "</script>");

        // 3 scripts: main + breadcrumb + inherited — all independent, not nested
        scriptOpenCount.Should().Be(3);
        scriptCloseCount.Should().Be(3);

        // Each script block should be independent — split by </script> and verify
        // each fragment contains exactly one opening <script> tag
        var fragments = html.Split("</script>");
        foreach (var fragment in fragments.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            var scriptStarts = CountOccurrences(fragment, "<script type=\"application/ld+json\">");
            scriptStarts.Should().Be(1, $"each fragment should contain exactly one script opening: '{fragment}'");
        }
    }

    [Fact]
    public void Process_WithBlockElementSchemas_OutputsSeparateScriptTags()
    {
        var content = Substitute.For<IPublishedContent>();
        var mainJson = """{"@type":"FAQPage"}""";
        var blockJson1 = """{"@type":"Question","name":"Q1"}""";
        var blockJson2 = """{"@type":"Question","name":"Q2"}""";

        _generator.GenerateJsonLdString(content).Returns(mainJson);
        _generator.GenerateBreadcrumbJsonLd(content).Returns((string?)null);
        _generator.GenerateInheritedJsonLdStrings(content).Returns([]);
        _generator.GenerateBlockElementJsonLdStrings(content).Returns([blockJson1, blockJson2]);

        var helper = CreateTagHelper();
        helper.Content = content;
        var (context, output) = CreateTagHelperContextAndOutput();

        helper.Process(context, output);

        var html = GetContentHtml(output);
        var scriptOpenCount = CountOccurrences(html, "<script type=\"application/ld+json\">");
        var scriptCloseCount = CountOccurrences(html, "</script>");

        scriptOpenCount.Should().Be(3);
        scriptCloseCount.Should().Be(3);
        html.Should().Contain(blockJson1);
        html.Should().Contain(blockJson2);
    }

    [Fact]
    public void Process_TagNameRemainsNull_NoWrappingElement()
    {
        var content = Substitute.For<IPublishedContent>();
        _generator.GenerateJsonLdString(content).Returns("""{"@type":"Article"}""");
        _generator.GenerateInheritedJsonLdStrings(content).Returns([]);
        _generator.GenerateBlockElementJsonLdStrings(content).Returns([]);

        var helper = CreateTagHelper();
        helper.Content = content;
        var (context, output) = CreateTagHelperContextAndOutput();

        helper.Process(context, output);

        // TagName should remain null — no wrapping element around the script tags
        output.TagName.Should().BeNull();
    }

    [Fact]
    public void Process_OutputsCorrectOrder_InheritedThenBreadcrumbThenMainThenBlocks()
    {
        var content = Substitute.For<IPublishedContent>();
        var inheritedJson = """{"@type":"WebSite","name":"My Site"}""";
        var breadcrumbJson = """{"@type":"BreadcrumbList"}""";
        var mainJson = """{"@type":"Product"}""";
        var blockJson = """{"@type":"Review"}""";

        _generator.GenerateJsonLdString(content).Returns(mainJson);
        _generator.GenerateBreadcrumbJsonLd(content).Returns(breadcrumbJson);
        _generator.GenerateInheritedJsonLdStrings(content).Returns([inheritedJson]);
        _generator.GenerateBlockElementJsonLdStrings(content).Returns([blockJson]);

        var helper = CreateTagHelper();
        helper.Content = content;
        var (context, output) = CreateTagHelperContextAndOutput();

        helper.Process(context, output);

        var html = GetContentHtml(output);

        // Verify ordering: inherited → breadcrumb → main → blocks
        var inheritedPos = html.IndexOf(inheritedJson, StringComparison.Ordinal);
        var breadcrumbPos = html.IndexOf(breadcrumbJson, StringComparison.Ordinal);
        var mainPos = html.IndexOf(mainJson, StringComparison.Ordinal);
        var blockPos = html.IndexOf(blockJson, StringComparison.Ordinal);

        inheritedPos.Should().BeGreaterThanOrEqualTo(0);
        breadcrumbPos.Should().BeGreaterThan(inheritedPos, "breadcrumb should come after inherited");
        mainPos.Should().BeGreaterThan(breadcrumbPos, "main should come after breadcrumb");
        blockPos.Should().BeGreaterThan(mainPos, "blocks should come after main");
    }

    [Fact]
    public void Process_WithOnlyInheritedSchemas_DoesNotSuppressOutput()
    {
        var content = Substitute.For<IPublishedContent>();
        var inheritedJson = """{"@type":"WebSite","name":"My Site"}""";

        _generator.GenerateJsonLdString(content).Returns((string?)null);
        _generator.GenerateBreadcrumbJsonLd(content).Returns((string?)null);
        _generator.GenerateInheritedJsonLdStrings(content).Returns([inheritedJson]);
        _generator.GenerateBlockElementJsonLdStrings(content).Returns([]);

        var helper = CreateTagHelper();
        helper.Content = content;
        var (context, output) = CreateTagHelperContextAndOutput();

        helper.Process(context, output);

        var html = GetContentHtml(output);
        html.Should().Contain(inheritedJson);
        CountOccurrences(html, "<script type=\"application/ld+json\">").Should().Be(1);
    }

    private static string GetContentHtml(TagHelperOutput output)
    {
        using var writer = new System.IO.StringWriter();
        output.Content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
