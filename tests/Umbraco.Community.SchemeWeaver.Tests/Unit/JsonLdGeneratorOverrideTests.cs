using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>The IContentMappingOverrideProvider seam consulted by JsonLdGenerator.</summary>
public class JsonLdGeneratorOverrideTests
{
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly IContentMappingOverrideProvider _overrides = Substitute.For<IContentMappingOverrideProvider>();
    private readonly IPublishedUrlProvider _urlProvider = Substitute.For<IPublishedUrlProvider>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly IPublishedContent _content;
    private readonly SchemaMapping _mapping;

    public JsonLdGeneratorOverrideTests()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("example.com");
        _httpContextAccessor.HttpContext.Returns(http);

        var type = Substitute.For<IPublishedContentType>();
        type.Alias.Returns("blogPost");
        _content = Substitute.For<IPublishedContent>();
        _content.ContentType.Returns(type);
        _content.Key.Returns(Guid.NewGuid());
        var headline = Substitute.For<IPublishedProperty>();
        headline.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("Hello");
        _content.GetProperty("title").Returns(headline);
        _urlProvider.GetUrl(_content, UrlMode.Absolute).Returns("https://example.com/blog/hello/");

        _mapping = new SchemaMapping { Id = 3, ContentTypeAlias = "blogPost", SchemaTypeName = "Article", IsEnabled = true };
        _repository.GetByContentTypeAlias("blogPost").Returns(_mapping);
        _repository.GetPropertyMappings(3).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "title" }
        });
    }

    private JsonLdGenerator Build(IContentMappingOverrideProvider? provider) => new(
        _repository,
        new SchemaTypeRegistry(),
        _httpContextAccessor,
        Substitute.For<IDocumentNavigationQueryService>(),
        Substitute.For<IPublishedContentStatusFilteringService>(),
        new PropertyValueResolverFactory([new DefaultPropertyValueResolver()]),
        _urlProvider,
        new SiteOriginResolver(_httpContextAccessor, Options.Create(new SchemeWeaverOptions()), NullLogger<SiteOriginResolver>.Instance),
        Substitute.For<IVariationContextAccessor>(),
        NullLogger<JsonLdGenerator>.Instance,
        Options.Create(new SchemeWeaverOptions()),
        provider);

    [Fact]
    public void NoProvider_BehavesAsBefore()
    {
        var thing = Build(provider: null).GenerateJsonLd(_content);
        thing.Should().BeOfType<Schema.NET.Article>();
        thing!.Id!.ToString().Should().EndWith("#article");
    }

    [Fact]
    public void ProviderReturnsNone_UsesTypeMapping()
    {
        _overrides.Resolve(_content, _mapping, null).Returns(MappingOverrideResult.None);
        Build(_overrides).GenerateJsonLd(_content).Should().BeOfType<Schema.NET.Article>();
    }

    [Fact]
    public void ProviderReturnsExcluded_EmitsNothing()
    {
        _overrides.Resolve(_content, _mapping, null).Returns(MappingOverrideResult.Excluded);
        Build(_overrides).GenerateJsonLd(_content).Should().BeNull();
    }

    [Fact]
    public void ProviderReturnsRetyped_UsesSubtype_KeepsPropertyMappings_AndDefaultId()
    {
        var clone = _mapping.Clone();
        clone.SchemaTypeName = "BlogPosting";
        _overrides.Resolve(_content, _mapping, "en-GB").Returns(MappingOverrideResult.Retyped(clone));

        var thing = Build(_overrides).GenerateJsonLd(_content, "en-GB");

        thing.Should().BeOfType<Schema.NET.BlogPosting>();
        System.Text.Json.JsonDocument.Parse(thing!.ToString()).RootElement.GetProperty("headline").GetString().Should().Be("Hello");
        thing.Id!.ToString().Should().EndWith("#blogposting");
        _mapping.SchemaTypeName.Should().Be("Article", "the cached mapping must never be mutated");
    }

    [Fact]
    public void ProviderIsNotConsulted_WhenMappingIsDisabled()
    {
        _mapping.IsEnabled = false;
        Build(_overrides).GenerateJsonLd(_content).Should().BeNull();
        _overrides.DidNotReceiveWithAnyArgs().Resolve(default!, default!, default);
    }
}
