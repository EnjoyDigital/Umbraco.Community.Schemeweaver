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

public class NumericResolverTests
{
    private readonly NumericResolver _sut = new();

    [Fact]
    public void SupportedEditorAliases_ContainsInteger()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.Integer");
    }

    [Fact]
    public void SupportedEditorAliases_ContainsDecimal()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.Decimal");
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
    public void Resolve_IntegerValue_ReturnsInt()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(42);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be(42);
        result.Should().BeOfType<int>();
    }

    [Fact]
    public void Resolve_DecimalValue_ReturnsDecimal()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(19.99m);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be(19.99m);
        result.Should().BeOfType<decimal>();
    }

    [Fact]
    public void Resolve_DoubleValue_ReturnsDouble()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(3.14);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be(3.14);
        result.Should().BeOfType<double>();
    }

    [Fact]
    public void Resolve_StringValue_ReturnsString()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("not a number");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("not a number");
    }

    private static PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "price" },
            PropertyAlias = "price",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = Substitute.For<IHttpContextAccessor>(),
            Property = property
        };
    }
}
