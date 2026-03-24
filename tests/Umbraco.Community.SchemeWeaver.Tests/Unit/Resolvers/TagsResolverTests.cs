using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class TagsResolverTests
{
    private readonly TagsResolver _sut = new();

    [Fact]
    public void SupportedEditorAliases_ContainsTags()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.Tags");
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
    public void Resolve_NullValue_ReturnsNull()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(null);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_MultipleTags_ReturnsCommaSeparated()
    {
        var tags = new List<string> { "CSharp", "Umbraco", "Schema.org" };
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(tags);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("CSharp, Umbraco, Schema.org");
    }

    [Fact]
    public void Resolve_SingleTag_ReturnsSingleString()
    {
        var tags = new List<string> { "Umbraco" };
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(tags);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("Umbraco");
    }

    [Fact]
    public void Resolve_EmptyTags_ReturnsNull()
    {
        var tags = new List<string>();
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(tags);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NonEnumerableValue_ReturnsToString()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("raw tags");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("raw tags");
    }

    private static PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "keywords" },
            PropertyAlias = "tags",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = Substitute.For<IHttpContextAccessor>(),
            Property = property
        };
    }
}
