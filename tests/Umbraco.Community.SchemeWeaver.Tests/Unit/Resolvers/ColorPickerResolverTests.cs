using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class ColorPickerResolverTests
{
    private readonly ColorPickerResolver _sut = new();

    [Fact]
    public void SupportedEditorAliases_ContainsColorPicker()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.ColorPicker");
    }

    [Fact]
    public void SupportedEditorAliases_ContainsEyeDropper()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.ColorPicker.EyeDropper");
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
    public void Resolve_PickedColor_ReturnsHashPrefixed()
    {
        var pickedColor = new ColorPickerValueConverter.PickedColor("ff0000", "Red");
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(pickedColor);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("#ff0000");
    }

    [Fact]
    public void Resolve_StringWithoutHash_AddsHashPrefix()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("00ff00");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("#00ff00");
    }

    [Fact]
    public void Resolve_StringWithHash_KeepsHashPrefix()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("#0000ff");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("#0000ff");
    }

    [Fact]
    public void Resolve_EmptyString_ReturnsNull()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    private static PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "color" },
            PropertyAlias = "brandColour",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = Substitute.For<IHttpContextAccessor>(),
            Property = property
        };
    }
}
