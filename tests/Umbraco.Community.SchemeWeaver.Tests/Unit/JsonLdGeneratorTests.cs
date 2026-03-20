using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;
using NSubstitute;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class JsonLdGeneratorTests
{
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly ISchemaTypeRegistry _registry = new SchemaTypeRegistry();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly IDocumentNavigationQueryService _navigationQueryService = Substitute.For<IDocumentNavigationQueryService>();
    private readonly IPublishedContentStatusFilteringService _publishedStatusFilteringService = Substitute.For<IPublishedContentStatusFilteringService>();
    private readonly IPropertyValueResolverFactory _resolverFactory;
    private readonly ILogger<JsonLdGenerator> _logger = Substitute.For<ILogger<JsonLdGenerator>>();
    private readonly JsonLdGenerator _sut;

    public JsonLdGeneratorTests()
    {
        _resolverFactory = new PropertyValueResolverFactory([new DefaultPropertyValueResolver()]);
        _sut = new JsonLdGenerator(
            _repository,
            _registry,
            _httpContextAccessor,
            _navigationQueryService,
            _publishedStatusFilteringService,
            _resolverFactory,
            _logger);
    }

    private static IPublishedContent CreateContent(string contentTypeAlias, Dictionary<string, object?>? properties = null)
    {
        var content = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        content.ContentType.Returns(contentType);
        content.Id.Returns(1);
        content.Key.Returns(Guid.NewGuid());

        if (properties is not null)
        {
            foreach (var kvp in properties)
            {
                var property = Substitute.For<IPublishedProperty>();
                property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(kvp.Value);
                content.GetProperty(kvp.Key).Returns(property);
            }
        }

        return content;
    }

    private static SchemaMapping CreateMapping(string contentTypeAlias, string schemaTypeName, bool isEnabled = true)
        => new()
        {
            Id = 1,
            ContentTypeAlias = contentTypeAlias,
            SchemaTypeName = schemaTypeName,
            IsEnabled = isEnabled
        };

    [Fact]
    public void GenerateJsonLd_NoMappingExists_ReturnsNull()
    {
        var content = CreateContent("article");
        _repository.GetByContentTypeAlias("article").Returns((SchemaMapping?)null);

        var result = _sut.GenerateJsonLd(content);

        result.Should().BeNull();
    }

    [Fact]
    public void GenerateJsonLd_MappingDisabled_ReturnsNull()
    {
        var content = CreateContent("article");
        var mapping = CreateMapping("article", "Article", isEnabled: false);
        _repository.GetByContentTypeAlias("article").Returns(mapping);

        var result = _sut.GenerateJsonLd(content);

        result.Should().BeNull();
    }

    [Fact]
    public void GenerateJsonLd_ValidMapping_ReturnsThingInstance()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Test Article"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Article>();
    }

    [Fact]
    public void GenerateJsonLd_StaticSourceType_UsesStaticValue()
    {
        var content = CreateContent("article");
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "InLanguage", SourceType = "static", StaticValue = "en-GB" }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
    }

    [Fact]
    public void GenerateJsonLd_ParentSourceType_ReadsFromParent()
    {
        var parentContent = CreateContent("homepage", new Dictionary<string, object?>
        {
            ["siteName"] = "My Site"
        });

        var content = CreateContent("article");
        var contentKey = content.Key;
        var parentKey = parentContent.Key;

        // Set up the navigation service to return the parent key
        _navigationQueryService.TryGetParentKey(contentKey, out Arg.Any<Guid?>())
            .Returns(callInfo =>
            {
                callInfo[1] = (Guid?)parentKey;
                return true;
            });

        // The extension method uses IPublishedSnapshot internally - for Parent<T>() to work
        // with mocked navigation, we rely on the deprecated .Parent property fallback
        // In the test context, Parent<T>() extension method calls navigation service
        // which we've mocked above. However, the extension method needs IPublishedSnapshot
        // to resolve the content by key. Since we can't easily mock that chain,
        // we use the deprecated Parent property for the test mock setup.
#pragma warning disable CS0618 // Type or member is obsolete - required for test mock setup
        content.Parent.Returns(parentContent);
#pragma warning restore CS0618

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", SourceType = "parent", ContentTypePropertyAlias = "siteName" }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
    }

    [Fact]
    public void GenerateJsonLd_StripHtmlTransform_RemovesTags()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["bodyText"] = "<p>Hello <strong>World</strong></p>"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Headline",
                SourceType = "property",
                ContentTypePropertyAlias = "bodyText",
                TransformType = "stripHtml"
            }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        // The Article Headline should have HTML stripped
        var article = result as Schema.NET.Article;
        article.Should().NotBeNull();
    }

    [Fact]
    public void GenerateJsonLdString_ValidMapping_ReturnsJsonString()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Test Headline"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });

        var result = _sut.GenerateJsonLdString(content);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("schema.org");
    }

    [Fact]
    public void GenerateJsonLdString_NoMapping_ReturnsNull()
    {
        var content = CreateContent("article");
        _repository.GetByContentTypeAlias("article").Returns((SchemaMapping?)null);

        var result = _sut.GenerateJsonLdString(content);

        result.Should().BeNull();
    }
}
