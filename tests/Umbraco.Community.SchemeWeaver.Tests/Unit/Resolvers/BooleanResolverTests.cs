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

public class BooleanResolverTests
{
    private readonly BooleanResolver _sut = new();

    [Fact]
    public void SupportedEditorAliases_ContainsTrueFalse()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.TrueFalse");
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
    public void Resolve_BoolTrue_ReturnsTrue()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(true);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be(true);
        result.Should().BeOfType<bool>();
    }

    [Fact]
    public void Resolve_BoolFalse_ReturnsFalse()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(false);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be(false);
        result.Should().BeOfType<bool>();
    }

    [Fact]
    public void Resolve_IntOne_ReturnsTrue()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(1);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be(true);
    }

    [Fact]
    public void Resolve_IntZero_ReturnsFalse()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(0);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be(false);
    }

    [Fact]
    public void Resolve_StringValue_ReturnsString()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("yes");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("yes");
    }

    private static PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "acceptsReservations" },
            PropertyAlias = "acceptsReservations",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = Substitute.For<IHttpContextAccessor>(),
            Property = property
        };
    }
}
