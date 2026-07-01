using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Schema.NET;
using Xunit;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.Routing;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class MediaPickerResolverTests
{
    private readonly IPublishedUrlProvider _urlProvider = Substitute.For<IPublishedUrlProvider>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly MediaPickerResolver _sut;

    static MediaPickerResolverTests()
    {
        // media.Value<int?>("umbracoWidth") flows through FriendlyPublishedContentExtensions,
        // which resolves IPublishedValueFallback from StaticServiceProvider. Ensure it is set
        // so the friendly extension does not throw during unit tests. Only set it when the
        // ambient provider cannot already satisfy the dependency (e.g. under integration hosts).
        if (StaticServiceProvider.Instance?.GetService<IPublishedValueFallback>() is null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPublishedValueFallback, NoopPublishedValueFallback>();
            StaticServiceProvider.Instance = services.BuildServiceProvider();
        }
    }

    public MediaPickerResolverTests()
    {
        _sut = new MediaPickerResolver(NullLogger<MediaPickerResolver>.Instance, _urlProvider);
    }

    /// <summary>
    /// Creates a media substitute whose absolute URL is served by <see cref="_urlProvider"/>,
    /// optionally stubbing the intrinsic <c>umbracoWidth</c>/<c>umbracoHeight</c> properties.
    /// </summary>
    private IPublishedContent CreateMediaContent(string url, int? width = null, int? height = null)
    {
        var media = Substitute.For<IPublishedContent>();
        _urlProvider
            .GetMediaUrl(media, UrlMode.Absolute, Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<Uri?>())
            .Returns(url);

        if (width is int w)
            StubIntProperty(media, "umbracoWidth", w);
        if (height is int h)
            StubIntProperty(media, "umbracoHeight", h);

        return media;
    }

    /// <summary>
    /// A media item whose URL cannot be resolved (deleted media / missing file) —
    /// the url provider returns null for it, so the factory yields no ImageObject.
    /// </summary>
    private static IPublishedContent CreateDamagedMedia() => Substitute.For<IPublishedContent>();

    private static void StubIntProperty(IPublishedContent media, string alias, int value)
    {
        var property = Substitute.For<IPublishedProperty>();
        property.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(true);
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value);
        media.GetProperty(alias).Returns(property);
    }

    private static MediaWithCrops WrapInCrops(IPublishedContent media) =>
        new(media, Substitute.For<IPublishedValueFallback>(), new ImageCropperValue());

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
    public void Resolve_SingleMedia_ReturnsImageObject_WithAbsoluteUrl()
    {
        var media = CreateMediaContent("https://example.com/media/1234/image.jpg");
        var mediaWithCrops = WrapInCrops(media);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(mediaWithCrops);

        var result = _sut.Resolve(CreateContext(property));

        var image = result.Should().BeOfType<ImageObject>().Subject;
        image.Url.First().Should().Be(new Uri("https://example.com/media/1234/image.jpg"));
    }

    [Fact]
    public void Resolve_MediaWithDimensions_SetsWidthAndHeight_AsQuantitativeValues()
    {
        var media = CreateMediaContent("https://example.com/media/1234/image.jpg", width: 800, height: 600);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(media);

        var result = _sut.Resolve(CreateContext(property));

        var image = result.Should().BeOfType<ImageObject>().Subject;
        var json = image.ToString();
        json.Should().Contain("QuantitativeValue");
        json.Should().Contain("width").And.Contain("800");
        json.Should().Contain("height").And.Contain("600");
    }

    [Fact]
    public void Resolve_MultipleMedia_ReturnsListOfImageObjects()
    {
        var first = CreateMediaContent("https://example.com/media/1/first.jpg");
        var second = CreateMediaContent("https://example.com/media/2/second.jpg");
        var items = new List<MediaWithCrops> { WrapInCrops(first), WrapInCrops(second) };

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(items);

        var result = _sut.Resolve(CreateContext(property));

        var images = result.Should().BeOfType<List<ImageObject>>().Subject;
        images.Should().HaveCount(2);
        images[0].Url.First().Should().Be(new Uri("https://example.com/media/1/first.jpg"));
        images[1].Url.First().Should().Be(new Uri("https://example.com/media/2/second.jpg"));
    }

    [Fact]
    public void Resolve_DamagedMedia_ReturnsNull()
    {
        // url provider is not stubbed for this media, so GetMediaUrl returns null
        var damaged = CreateDamagedMedia();

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(damaged);

        var result = _sut.Resolve(CreateContext(property));

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_FirstOfManyDamaged_SkipsAndReturnsRest()
    {
        var damaged = CreateDamagedMedia();
        var healthy = CreateMediaContent("https://example.com/media/2/second.jpg");
        var items = new List<MediaWithCrops> { WrapInCrops(damaged), WrapInCrops(healthy) };

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(items);

        var result = _sut.Resolve(CreateContext(property));

        // one damaged is dropped, leaving a single healthy image (returned unwrapped)
        var image = result.Should().BeOfType<ImageObject>().Subject;
        image.Url.First().Should().Be(new Uri("https://example.com/media/2/second.jpg"));
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
