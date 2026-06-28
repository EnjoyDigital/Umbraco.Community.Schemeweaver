using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Umbraco.Community.SchemeWeaver.Services.Transforms;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Transforms;

public class SchemaValueTransformerTests
{
    [Fact]
    public void Apply_StripHtml_RemovesTagsAndTrims()
    {
        var result = SchemaValueTransformer.Apply(
            "  <p>Because <strong>schema</strong>.</p>  ", "stripHtml", httpContextAccessor: null);

        result.Should().Be("Because schema.");
    }

    [Fact]
    public void Apply_ToAbsoluteUrl_PrefixesSchemeAndHost()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        accessor.HttpContext.Returns(httpContext);

        var result = SchemaValueTransformer.Apply("/about-us", "toAbsoluteUrl", accessor);

        result.Should().Be("https://example.com/about-us");
    }

    [Fact]
    public void Apply_ToAbsoluteUrl_AlreadyAbsolute_ReturnsUnchanged()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext());

        var result = SchemaValueTransformer.Apply("https://other.test/page", "toAbsoluteUrl", accessor);

        result.Should().Be("https://other.test/page");
    }

    [Fact]
    public void Apply_ToAbsoluteUrl_NoHttpContext_ReturnsOriginal()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        var result = SchemaValueTransformer.Apply("/about-us", "toAbsoluteUrl", accessor);

        result.Should().Be("/about-us");
    }

    [Fact]
    public void Apply_FormatDate_Valid_NormalizesToIsoDate()
    {
        var result = SchemaValueTransformer.Apply("20 March 2024", "formatDate", httpContextAccessor: null);

        result.Should().Be("2024-03-20");
    }

    [Fact]
    public void Apply_FormatDate_Invalid_ReturnsInput()
    {
        var result = SchemaValueTransformer.Apply("not a date", "formatDate", httpContextAccessor: null);

        result.Should().Be("not a date");
    }

    [Fact]
    public void Apply_UnknownTransform_ReturnsInput()
    {
        var result = SchemaValueTransformer.Apply("value", "doesNotExist", httpContextAccessor: null);

        result.Should().Be("value");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Apply_NoTransformType_ReturnsInput(string? transformType)
    {
        var result = SchemaValueTransformer.Apply("value", transformType, httpContextAccessor: null);

        result.Should().Be("value");
    }

    [Fact]
    public void Apply_NullValue_ReturnsNull()
    {
        var result = SchemaValueTransformer.Apply(null, "stripHtml", httpContextAccessor: null);

        result.Should().BeNull();
    }
}
