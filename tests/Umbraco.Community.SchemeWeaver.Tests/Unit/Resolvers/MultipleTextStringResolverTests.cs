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

public class MultipleTextStringResolverTests
{
    private readonly MultipleTextStringResolver _sut = new();

    [Fact]
    public void SupportedEditorAliases_ContainsMultipleTextstring()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.MultipleTextstring");
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
    public void Resolve_MultipleStrings_ReturnsList()
    {
        var strings = new List<string> { "Line 1", "Line 2", "Line 3" };
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(strings);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeAssignableTo<IEnumerable<string>>();
        var list = ((IEnumerable<string>)result!).ToList();
        list.Should().HaveCount(3);
        list.Should().ContainInOrder("Line 1", "Line 2", "Line 3");
    }

    [Fact]
    public void Resolve_SingleString_ReturnsSingleElementList()
    {
        var strings = new List<string> { "Only line" };
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(strings);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeAssignableTo<IEnumerable<string>>();
        var list = ((IEnumerable<string>)result!).ToList();
        list.Should().ContainSingle().Which.Should().Be("Only line");
    }

    [Fact]
    public void Resolve_EmptyList_ReturnsNull()
    {
        var strings = new List<string>();
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(strings);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NonEnumerableValue_ReturnsToString()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(42);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("42");
    }

    private static PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "recipeIngredient" },
            PropertyAlias = "ingredients",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = Substitute.For<IHttpContextAccessor>(),
            Property = property
        };
    }
}
