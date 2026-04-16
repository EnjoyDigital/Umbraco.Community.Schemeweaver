using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class BuiltInPropertyResolverTests
{
    private readonly IPublishedUrlProvider _urlProvider = Substitute.For<IPublishedUrlProvider>();
    private readonly BuiltInPropertyResolver _sut;

    public BuiltInPropertyResolverTests()
    {
        _sut = new BuiltInPropertyResolver(_urlProvider);
    }

    [Fact]
    public void SupportedEditorAliases_ReturnsBuiltInAlias()
    {
        _sut.SupportedEditorAliases.Should().ContainSingle()
            .Which.Should().Be(SchemeWeaverConstants.BuiltInProperties.EditorAlias);
    }

    [Fact]
    public void Priority_IsHigherThanDefault()
    {
        _sut.Priority.Should().Be(20);
    }

    [Fact]
    public void Resolve_UrlAlias_ReturnsAbsoluteUrl()
    {
        var content = Substitute.For<IPublishedContent>();
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("https://example.com/test-page");

        var context = CreateContext(content, SchemeWeaverConstants.BuiltInProperties.Url);
        var result = _sut.Resolve(context);

        result.Should().Be("https://example.com/test-page");
    }

    [Fact]
    public void Resolve_UrlAlias_AbsoluteReturnsHash_FallsBackToRelative()
    {
        var content = Substitute.For<IPublishedContent>();
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("#");
        _urlProvider.GetUrl(content, UrlMode.Relative).Returns("/test-page");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var context = CreateContext(content, SchemeWeaverConstants.BuiltInProperties.Url, httpContextAccessor);
        var result = _sut.Resolve(context);

        result.Should().Be("https://example.com/test-page");
    }

    [Fact]
    public void Resolve_UrlAlias_BothReturnHash_ReturnsNull()
    {
        var content = Substitute.For<IPublishedContent>();
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("#");
        _urlProvider.GetUrl(content, UrlMode.Relative).Returns("#");

        var context = CreateContext(content, SchemeWeaverConstants.BuiltInProperties.Url);
        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NameAlias_ReturnsContentName()
    {
        var content = Substitute.For<IPublishedContent>();
        content.Name.Returns("My Test Page");

        var context = CreateContext(content, SchemeWeaverConstants.BuiltInProperties.Name);
        var result = _sut.Resolve(context);

        result.Should().Be("My Test Page");
    }

    [Fact]
    public void Resolve_CreateDateAlias_ReturnsIso8601()
    {
        var content = Substitute.For<IPublishedContent>();
        content.CreateDate.Returns(new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Utc));

        var context = CreateContext(content, SchemeWeaverConstants.BuiltInProperties.CreateDate);
        var result = _sut.Resolve(context);

        result.Should().NotBeNull().And.BeOfType<string>();
        var resultString = (string)result!;
        resultString.Should().Contain("2024-03-15");
        resultString.Should().Contain("10:30:00");
    }

    [Fact]
    public void Resolve_UpdateDateAlias_ReturnsIso8601()
    {
        var content = Substitute.For<IPublishedContent>();
        content.UpdateDate.Returns(new DateTime(2024, 6, 20, 14, 45, 0, DateTimeKind.Utc));

        var context = CreateContext(content, SchemeWeaverConstants.BuiltInProperties.UpdateDate);
        var result = _sut.Resolve(context);

        result.Should().NotBeNull().And.BeOfType<string>();
        var resultString = (string)result!;
        resultString.Should().Contain("2024-06-20");
        resultString.Should().Contain("14:45:00");
    }

    [Fact]
    public void Resolve_UnknownAlias_ReturnsNull()
    {
        var content = Substitute.For<IPublishedContent>();

        var context = CreateContext(content, "__unknown");
        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    private static PropertyResolverContext CreateContext(
        IPublishedContent content,
        string propertyAlias,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        return new PropertyResolverContext
        {
            Content = content,
            Mapping = new PropertyMapping
            {
                SchemaPropertyName = "testProp",
                ContentTypePropertyAlias = propertyAlias,
                SourceType = "property"
            },
            PropertyAlias = propertyAlias,
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = httpContextAccessor ?? Substitute.For<IHttpContextAccessor>(),
            Property = null
        };
    }
}
