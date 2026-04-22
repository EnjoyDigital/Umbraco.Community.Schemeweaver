using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Validation;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class SchemeWeaverServiceTests
{
    private readonly ISchemaTypeRegistry _registry = Substitute.For<ISchemaTypeRegistry>();
    private readonly ISchemaAutoMapper _autoMapper = Substitute.For<ISchemaAutoMapper>();
    private readonly IJsonLdGenerator _generator = Substitute.For<IJsonLdGenerator>();
    private readonly IGraphGenerator _graphGenerator = Substitute.For<IGraphGenerator>();
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly IContentTypeService _contentTypeService = Substitute.For<IContentTypeService>();
    private readonly IDataTypeService _dataTypeService = Substitute.For<IDataTypeService>();
    private readonly ISchemaValidator _validator = Substitute.For<ISchemaValidator>();
    private readonly ILogger<SchemeWeaverService> _logger = Substitute.For<ILogger<SchemeWeaverService>>();

    // Existing preview tests assert against the legacy single-Thing string;
    // keep the graph model off so their assertions stay meaningful. A
    // dedicated @graph preview test below flips this on.
    private readonly SchemeWeaverOptions _options = new() { UseGraphModel = false };

    private readonly SchemeWeaverService _sut;

    public SchemeWeaverServiceTests()
    {
        // Default to an empty validation result — tests that care about issues
        // override this per-test. Without it, NSubstitute returns null and
        // ApplyValidation NPEs before the assertion runs.
        _validator.Validate(Arg.Any<string>()).Returns(ValidationResult.Empty);

        _sut = new SchemeWeaverService(
            _registry, _autoMapper, _generator, _graphGenerator,
            _repository, _contentTypeService, _dataTypeService,
            _validator, Options.Create(_options), _logger);
    }

    [Fact]
    public void GetMapping_DelegatesToRepository()
    {
        var mapping = new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        var result = _sut.GetMapping("article");

        result.Should().NotBeNull();
        result!.ContentTypeAlias.Should().Be("article");
        result.SchemaTypeName.Should().Be("Article");
        _repository.Received(1).GetByContentTypeAlias("article");
    }

    [Fact]
    public void GetMapping_NoMapping_ReturnsNull()
    {
        _repository.GetByContentTypeAlias("unknown").Returns((SchemaMapping?)null);

        var result = _sut.GetMapping("unknown");

        result.Should().BeNull();
    }

    [Fact]
    public void AutoMap_DelegatesToAutoMapper()
    {
        var suggestions = new List<PropertyMappingSuggestion>
        {
            new() { SchemaPropertyName = "headline", Confidence = 100 }
        };
        _autoMapper.SuggestMappings("article", "Article").Returns(suggestions);

        var result = _sut.AutoMap("article", "Article").ToList();

        result.Should().HaveCount(1);
        _autoMapper.Received(1).SuggestMappings("article", "Article");
    }

    [Fact]
    public void GeneratePreview_DelegatesToGenerator()
    {
        var content = Substitute.For<IPublishedContent>();
        content.Id.Returns(1);
        _generator.GenerateJsonLdString(content).Returns("{\"@type\": \"Article\"}");

        var result = _sut.GeneratePreview(content);

        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.JsonLd.Should().Contain("Article");
        _generator.Received(1).GenerateJsonLdString(content);
    }

    [Fact]
    public void GeneratePreview_GraphModelEnabled_RoutesThroughGraphGenerator()
    {
        _options.UseGraphModel = true;
        var content = Substitute.For<IPublishedContent>();
        _graphGenerator.GenerateGraphJson(content, null).Returns("{\"@graph\":[{\"@type\":\"Organization\"}]}");

        var result = _sut.GeneratePreview(content);

        result.IsValid.Should().BeTrue();
        result.JsonLd.Should().Contain("@graph");
        _graphGenerator.Received(1).GenerateGraphJson(content, null);
        _generator.DidNotReceive().GenerateJsonLdString(Arg.Any<IPublishedContent>(), Arg.Any<string?>());
    }

    [Fact]
    public void SearchSchemaTypes_DelegatesToRegistry()
    {
        var types = new List<SchemaTypeInfo>
        {
            new() { Name = "Article" }
        };
        _registry.Search("Art").Returns(types);

        var result = _sut.SearchSchemaTypes("Art").ToList();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Article");
        _registry.Received(1).Search("Art");
    }

    [Fact]
    public void SaveMapping_DelegatesToRepository()
    {
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingDto>()
        };

        var savedEntity = new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        var result = _sut.SaveMapping(dto);

        result.Should().NotBeNull();
        _repository.Received(1).Save(Arg.Any<SchemaMapping>());
        _repository.Received(1).SavePropertyMappings(1, Arg.Any<IEnumerable<PropertyMapping>>());
    }

    [Fact]
    public void SaveMapping_WithMultipleProperties_PreservesAll()
    {
        // Round-trips a mapping containing many distinct property mappings
        // through SaveMapping → SavePropertyMappings to make sure none are
        // dropped along the way. This regression test exists because an early
        // E2E test corrupted seeded data by accidentally writing back a
        // single-property mapping; that case never reached the C# layer but
        // the safety net belongs here.
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingDto>
            {
                new() { SchemaPropertyName = "headline", SourceType = "property", ContentTypePropertyAlias = "title" },
                new() { SchemaPropertyName = "description", SourceType = "property", ContentTypePropertyAlias = "summary" },
                new() { SchemaPropertyName = "image", SourceType = "property", ContentTypePropertyAlias = "heroImage" },
                new() { SchemaPropertyName = "author", SourceType = "static", StaticValue = "Editorial Team" },
                new()
                {
                    SchemaPropertyName = "publisher",
                    SourceType = "parent",
                    SourceContentTypeAlias = "siteRoot",
                    DynamicRootConfig = """{"originAlias":"Root","querySteps":[]}"""
                },
                new()
                {
                    SchemaPropertyName = "review",
                    SourceType = "blockContent",
                    ContentTypePropertyAlias = "reviews",
                    NestedSchemaTypeName = "Review",
                    ResolverConfig = """{"nestedMappings":[{"schemaProperty":"Author","contentProperty":"reviewAuthor"}]}"""
                },
            }
        };

        var savedEntity = new SchemaMapping
        {
            Id = 42,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(42).Returns(Enumerable.Empty<PropertyMapping>());

        List<PropertyMapping>? captured = null;
        _repository
            .When(r => r.SavePropertyMappings(42, Arg.Any<IEnumerable<PropertyMapping>>()))
            .Do(c => captured = c.Arg<IEnumerable<PropertyMapping>>().ToList());

        _sut.SaveMapping(dto);

        captured.Should().NotBeNull();
        captured!.Should().HaveCount(6, "all six property mappings must reach the repository");
        captured.Select(m => m.SchemaPropertyName).Should().BeEquivalentTo(new[]
        {
            "headline", "description", "image", "author", "publisher", "review"
        });
        captured.Single(m => m.SchemaPropertyName == "publisher").DynamicRootConfig
            .Should().Be("""{"originAlias":"Root","querySteps":[]}""");
        captured.Single(m => m.SchemaPropertyName == "review").ResolverConfig
            .Should().Contain("reviewAuthor");
        captured.Single(m => m.SchemaPropertyName == "author").StaticValue
            .Should().Be("Editorial Team");
    }

    [Fact]
    public void SaveMapping_WithDynamicRootConfig_PersistsField()
    {
        const string dynamicRootJson = """{"originAlias":"Root","querySteps":[]}""";

        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingDto>
            {
                new()
                {
                    SchemaPropertyName = "publisher",
                    SourceType = "parent",
                    SourceContentTypeAlias = "organization",
                    DynamicRootConfig = dynamicRootJson
                }
            }
        };

        var savedEntity = new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        List<PropertyMapping>? capturedMappings = null;
        _repository
            .When(r => r.SavePropertyMappings(1, Arg.Any<IEnumerable<PropertyMapping>>()))
            .Do(c => capturedMappings = c.Arg<IEnumerable<PropertyMapping>>().ToList());

        var result = _sut.SaveMapping(dto);

        result.Should().NotBeNull();
        capturedMappings.Should().NotBeNull();
        capturedMappings!.Should().HaveCount(1);
        capturedMappings[0].SchemaPropertyName.Should().Be("publisher");
        capturedMappings[0].SourceType.Should().Be("parent");
        capturedMappings[0].SourceContentTypeAlias.Should().Be("organization");
        capturedMappings[0].DynamicRootConfig.Should().Be(dynamicRootJson);
    }

    [Fact]
    public void GetMapping_ReturnsDynamicRootConfig()
    {
        const string dynamicRootJson = """{"originAlias":"Root"}""";

        var mapping = new SchemaMapping
        {
            Id = 7,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        var propertyMappings = new List<PropertyMapping>
        {
            new()
            {
                Id = 100,
                SchemaMappingId = 7,
                SchemaPropertyName = "publisher",
                SourceType = "parent",
                DynamicRootConfig = dynamicRootJson
            }
        };
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(7).Returns(propertyMappings);

        var result = _sut.GetMapping("article");

        result.Should().NotBeNull();
        result!.PropertyMappings.Should().HaveCount(1);
        result.PropertyMappings[0].DynamicRootConfig.Should().Be(dynamicRootJson);
    }

    [Fact]
    public void SaveMapping_ResolvesContentTypeKey_WhenEmpty()
    {
        var expectedKey = Guid.NewGuid();
        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "article",
            ContentTypeKey = Guid.Empty,
            SchemaTypeName = "Article",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingDto>()
        };

        var contentType = Substitute.For<IContentType>();
        contentType.Key.Returns(expectedKey);
        _contentTypeService.Get("article").Returns(contentType);

        var savedEntity = new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            ContentTypeKey = expectedKey,
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(null as SchemaMapping, savedEntity);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(savedEntity);
        _repository.GetPropertyMappings(1).Returns(Enumerable.Empty<PropertyMapping>());

        var result = _sut.SaveMapping(dto);

        result.Should().NotBeNull();
        _contentTypeService.Received(1).Get("article");
        _repository.Received(1).Save(Arg.Is<SchemaMapping>(m => m.ContentTypeKey == expectedKey));
    }
}
