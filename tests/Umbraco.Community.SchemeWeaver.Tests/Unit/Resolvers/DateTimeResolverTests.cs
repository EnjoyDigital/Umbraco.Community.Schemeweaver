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

public class DateTimeResolverTests
{
    private readonly DateTimeResolver _sut = new();

    [Fact]
    public void SupportedEditorAliases_ContainsDateTime()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.DateTime");
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
    public void Resolve_DateTime_ReturnsIso8601String()
    {
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(dt);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeOfType<string>();
        var resultString = (string)result!;
        resultString.Should().Contain("2024-06-15");
        resultString.Should().Contain("10:30:00");
    }

    [Fact]
    public void Resolve_DateTimeOffset_ReturnsIso8601String()
    {
        var dto = new DateTimeOffset(2024, 12, 25, 14, 0, 0, TimeSpan.FromHours(1));
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(dto);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeOfType<string>();
        var resultString = (string)result!;
        resultString.Should().Contain("2024-12-25");
        resultString.Should().Contain("14:00:00");
    }

    [Fact]
    public void Resolve_StringValue_ReturnsString()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("2024-01-01");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("2024-01-01");
    }

    private static PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "datePublished" },
            PropertyAlias = "publishDate",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = Substitute.For<IHttpContextAccessor>(),
            Property = property
        };
    }
}
