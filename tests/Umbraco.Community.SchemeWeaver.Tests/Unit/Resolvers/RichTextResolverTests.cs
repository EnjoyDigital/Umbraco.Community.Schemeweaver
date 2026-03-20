using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Strings;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class RichTextResolverTests
{
    private readonly RichTextResolver _sut = new();

    [Fact]
    public void SupportedEditorAliases_ContainsRichText()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.RichText");
    }

    [Fact]
    public void SupportedEditorAliases_ContainsTinyMCE()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.TinyMCE");
    }

    [Fact]
    public void SupportedEditorAliases_ContainsMarkdownEditor()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.MarkdownEditor");
    }

    [Fact]
    public void Priority_Returns10()
    {
        _sut.Priority.Should().Be(10);
    }

    [Fact]
    public void Resolve_NullProperty_ReturnsNull()
    {
        var context = CreateContext(null);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NullPropertyValue_ReturnsNull()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(null);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_HtmlEncodedString_ReturnsHtmlString()
    {
        var htmlString = new HtmlEncodedString("<p>Hello <strong>World</strong></p>");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(htmlString);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("<p>Hello <strong>World</strong></p>");
    }

    [Fact]
    public void Resolve_IHtmlEncodedString_ReturnsHtmlString()
    {
        var htmlString = Substitute.For<IHtmlEncodedString>();
        htmlString.ToHtmlString().Returns("<p>Substituted HTML</p>");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(htmlString);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("<p>Substituted HTML</p>");
    }

    [Fact]
    public void Resolve_PlainString_ReturnsString()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("# Markdown Heading");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("# Markdown Heading");
    }

    [Fact]
    public void Resolve_EmptyHtmlString_ReturnsEmptyString()
    {
        var htmlString = new HtmlEncodedString(string.Empty);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(htmlString);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be(string.Empty);
    }

    [Fact]
    public void Resolve_NonStringObject_ReturnsToString()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(12345);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("12345");
    }

    private static PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "ArticleBody" },
            PropertyAlias = "bodyText",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = Substitute.For<IHttpContextAccessor>(),
            Property = property
        };
    }
}
