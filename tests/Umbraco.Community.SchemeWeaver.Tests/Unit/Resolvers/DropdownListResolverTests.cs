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

public class DropdownListResolverTests
{
    private readonly DropdownListResolver _sut = new();

    [Fact]
    public void SupportedEditorAliases_ContainsDropDownFlexible()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.DropDown.Flexible");
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
    public void Resolve_SingleSelection_ReturnsSingleString()
    {
        var items = new List<string> { "Full-time" };
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(items);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("Full-time");
    }

    [Fact]
    public void Resolve_MultipleSelections_ReturnsCommaSeparated()
    {
        var items = new List<string> { "Full-time", "Part-time", "Contract" };
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(items);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("Full-time, Part-time, Contract");
    }

    [Fact]
    public void Resolve_EmptyList_ReturnsNull()
    {
        var items = new List<string>();
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(items);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NonEnumerableValue_ReturnsToString()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("raw value");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("raw value");
    }

    private static PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "employmentType" },
            PropertyAlias = "jobType",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = Substitute.For<IHttpContextAccessor>(),
            Property = property
        };
    }
}
