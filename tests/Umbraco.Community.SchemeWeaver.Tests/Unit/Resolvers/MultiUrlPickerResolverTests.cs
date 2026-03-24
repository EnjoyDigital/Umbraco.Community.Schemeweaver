using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class MultiUrlPickerResolverTests
{
    private readonly MultiUrlPickerResolver _sut = new();

    [Fact]
    public void SupportedEditorAliases_ContainsMultiUrlPicker()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.MultiUrlPicker");
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
    public void Resolve_ExternalUrl_ReturnsAbsoluteUrl()
    {
        var links = new List<Link> { new() { Url = "https://example.com/page", Name = "Example" } };
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(links);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("https://example.com/page");
    }

    [Fact]
    public void Resolve_RelativeUrl_ConvertsToAbsolute()
    {
        var links = new List<Link> { new() { Url = "/about-us/", Name = "About" } };
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(links);

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = Substitute.For<HttpContext>();
        var request = Substitute.For<HttpRequest>();
        request.Scheme.Returns("https");
        request.Host.Returns(new HostString("www.example.com"));
        httpContext.Request.Returns(request);
        httpContextAccessor.HttpContext.Returns(httpContext);

        var context = CreateContext(property, httpContextAccessor);

        var result = _sut.Resolve(context);

        result.Should().Be("https://www.example.com/about-us/");
    }

    [Fact]
    public void Resolve_EmptyLinks_ReturnsNull()
    {
        var links = new List<Link>();
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(links);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_LinkWithNullUrl_ReturnsNull()
    {
        var links = new List<Link> { new() { Url = null, Name = "Empty" } };
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(links);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_SingleLink_ReturnsSingleUrl()
    {
        var link = new Link { Url = "https://example.com", Name = "Example" };
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(link);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("https://example.com");
    }

    [Fact]
    public void Resolve_NonLinkValue_ReturnsToString()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("plain url");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("plain url");
    }

    private static PropertyResolverContext CreateContext(
        IPublishedProperty? property,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "url" },
            PropertyAlias = "ticketUrl",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = httpContextAccessor ?? Substitute.For<IHttpContextAccessor>(),
            Property = property
        };
    }
}
