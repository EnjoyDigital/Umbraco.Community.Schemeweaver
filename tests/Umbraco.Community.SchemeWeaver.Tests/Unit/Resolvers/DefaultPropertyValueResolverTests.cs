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

public class DefaultPropertyValueResolverTests
{
    private readonly DefaultPropertyValueResolver _sut = new();

    [Fact]
    public void SupportedEditorAliases_ReturnsEmpty()
    {
        _sut.SupportedEditorAliases.Should().BeEmpty();
    }

    [Fact]
    public void Priority_ReturnsZero()
    {
        _sut.Priority.Should().Be(0);
    }

    [Fact]
    public void Resolve_PropertyHasValue_ReturnsStringRepresentation()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("Test Value");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("Test Value");
    }

    [Fact]
    public void Resolve_PropertyHasNullValue_ReturnsNull()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(null);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_PropertyIsNull_ReturnsNull()
    {
        var context = CreateContext(null);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_IntegerValue_ReturnsStringRepresentation()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(42);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("42");
    }

    [Fact]
    public void Resolve_WithCulture_PassesCultureToGetValue()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue("de-DE", null).Returns("German Value");
        property.GetValue(null, null).Returns("Default Value");

        var content = Substitute.For<IPublishedContent>();
        var context = new PropertyResolverContext
        {
            Content = content,
            Mapping = new PropertyMapping { SchemaPropertyName = "TestProp" },
            PropertyAlias = "testProp",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = Substitute.For<IHttpContextAccessor>(),
            Property = property,
            Culture = "de-DE"
        };

        var result = _sut.Resolve(context);

        result.Should().Be("German Value");
    }

    private static PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        var content = Substitute.For<IPublishedContent>();
        return new PropertyResolverContext
        {
            Content = content,
            Mapping = new PropertyMapping { SchemaPropertyName = "TestProp" },
            PropertyAlias = "testProp",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = Substitute.For<IHttpContextAccessor>(),
            Property = property
        };
    }
}
