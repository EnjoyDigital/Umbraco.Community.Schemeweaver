using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class MediaPickerResolverTests
{
    private readonly MediaPickerResolver _sut = new(NullLogger<MediaPickerResolver>.Instance);
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MediaPickerResolverTests()
    {
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        _httpContextAccessor.HttpContext.Returns(httpContext);
    }

    private static IPublishedContent CreateMediaContent(string url)
    {
        var umbracoFileProperty = Substitute.For<IPublishedProperty>();
        umbracoFileProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(url);

        var media = Substitute.For<IPublishedContent>();
        media.GetProperty("umbracoFile").Returns(umbracoFileProperty);
        return media;
    }

    private static IPublishedContent CreateMediaContentWithCropper(string url)
    {
        var cropperValue = new ImageCropperValue { Src = url };
        var umbracoFileProperty = Substitute.For<IPublishedProperty>();
        umbracoFileProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(cropperValue);

        var media = Substitute.For<IPublishedContent>();
        media.GetProperty("umbracoFile").Returns(umbracoFileProperty);
        return media;
    }

    [Fact]
    public void SupportedEditorAliases_ContainsMediaPicker3()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.MediaPicker3");
    }

    [Fact]
    public void SupportedEditorAliases_ContainsLegacyMediaPicker()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.MediaPicker");
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
    public void Resolve_NullPropertyValue_ReturnsNull()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(null);

        var context = CreateContext(property);
        var result = _sut.Resolve(context);
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_SingleMediaWithCrops_ReturnsAbsoluteUrl()
    {
        var mediaContent = CreateMediaContent("/media/1234/image.jpg");
        var publishedValueFallback = Substitute.For<IPublishedValueFallback>();
        var localCrops = new ImageCropperValue();
        var mediaWithCrops = new MediaWithCrops(mediaContent, publishedValueFallback, localCrops);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(mediaWithCrops);

        var context = CreateContext(property);
        var result = _sut.Resolve(context);

        result.Should().Be("https://example.com/media/1234/image.jpg");
    }

    [Fact]
    public void Resolve_MultipleMediaWithCrops_ReturnsFirstItemUrl()
    {
        var mediaContent = CreateMediaContent("/media/1234/first.jpg");
        var secondContent = CreateMediaContent("/media/5678/second.jpg");
        var publishedValueFallback = Substitute.For<IPublishedValueFallback>();
        var localCrops = new ImageCropperValue();
        var mediaWithCrops = new MediaWithCrops(mediaContent, publishedValueFallback, localCrops);
        var secondMediaWithCrops = new MediaWithCrops(secondContent, publishedValueFallback, localCrops);

        var items = new List<MediaWithCrops> { mediaWithCrops, secondMediaWithCrops };

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(items);

        var context = CreateContext(property);
        var result = _sut.Resolve(context);

        result.Should().Be("https://example.com/media/1234/first.jpg");
    }

    [Fact]
    public void Resolve_PublishedContent_ReturnsAbsoluteUrl()
    {
        var mediaContent = CreateMediaContent("/media/1234/image.jpg");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(mediaContent);

        var context = CreateContext(property);
        var result = _sut.Resolve(context);

        result.Should().Be("https://example.com/media/1234/image.jpg");
    }

    [Fact]
    public void Resolve_AbsoluteUrl_ReturnsAsIs()
    {
        var mediaContent = CreateMediaContent("https://cdn.example.com/image.jpg");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(mediaContent);

        var context = CreateContext(property);
        var result = _sut.Resolve(context);

        result.Should().Be("https://cdn.example.com/image.jpg");
    }

    [Fact]
    public void Resolve_ImageCropperValue_ExtractsSrc()
    {
        var mediaContent = CreateMediaContentWithCropper("/media/1234/cropped.jpg");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(mediaContent);

        var context = CreateContext(property);
        var result = _sut.Resolve(context);

        result.Should().Be("https://example.com/media/1234/cropped.jpg");
    }

    [Fact]
    public void Resolve_MediaWithNoUmbracoFile_ReturnsNull()
    {
        var mediaContent = Substitute.For<IPublishedContent>();
        mediaContent.GetProperty("umbracoFile").Returns((IPublishedProperty?)null);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(mediaContent);

        var context = CreateContext(property);
        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NoHttpContext_ReturnsRelativeUrl()
    {
        var noHttpAccessor = Substitute.For<IHttpContextAccessor>();
        noHttpAccessor.HttpContext.Returns((HttpContext?)null);

        var mediaContent = CreateMediaContent("/media/1234/image.jpg");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(mediaContent);

        var context = new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "Image" },
            PropertyAlias = "image",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = noHttpAccessor,
            Property = property
        };

        var result = _sut.Resolve(context);

        result.Should().Be("/media/1234/image.jpg");
    }

    [Fact]
    public void Resolve_EmptyMultipleMediaList_ReturnsNull()
    {
        var items = new List<MediaWithCrops>();

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(items);

        var context = CreateContext(property);
        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_MultipleMediaWithCrops_FirstItemDeletedMedia_ReturnsNull()
    {
        // When the first item in a multi-value picker has a deleted/broken media item
        // (umbracoFile property missing), the resolver should return null gracefully
        var deletedMedia = Substitute.For<IPublishedContent>();
        deletedMedia.GetProperty("umbracoFile").Returns((IPublishedProperty?)null);

        var publishedValueFallback = Substitute.For<IPublishedValueFallback>();
        var localCrops = new ImageCropperValue();
        var mediaWithCrops = new MediaWithCrops(deletedMedia, publishedValueFallback, localCrops);

        var items = new List<MediaWithCrops> { mediaWithCrops };

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(items);

        var context = CreateContext(property);
        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_UmbracoFilePropertyValueIsNull_ReturnsNull()
    {
        // When umbracoFile property exists on the media but its value is null
        // (e.g., media item exists but the file has been removed from disk)
        var umbracoFileProperty = Substitute.For<IPublishedProperty>();
        umbracoFileProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(null);

        var media = Substitute.For<IPublishedContent>();
        media.GetProperty("umbracoFile").Returns(umbracoFileProperty);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(media);

        var context = CreateContext(property);
        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    private PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "Image" },
            PropertyAlias = "image",
            SchemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>(),
            MappingRepository = Substitute.For<ISchemaMappingRepository>(),
            HttpContextAccessor = _httpContextAccessor,
            Property = property
        };
    }
}
