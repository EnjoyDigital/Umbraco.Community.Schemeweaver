using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using NSubstitute;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class SchemeWeaverServiceTests
{
    private readonly ISchemaTypeRegistry _registry = Substitute.For<ISchemaTypeRegistry>();
    private readonly ISchemaAutoMapper _autoMapper = Substitute.For<ISchemaAutoMapper>();
    private readonly IJsonLdGenerator _generator = Substitute.For<IJsonLdGenerator>();
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly ILogger<SchemeWeaverService> _logger = Substitute.For<ILogger<SchemeWeaverService>>();
    private readonly SchemeWeaverService _sut;

    public SchemeWeaverServiceTests()
    {
        _sut = new SchemeWeaverService(_registry, _autoMapper, _generator, _repository, _logger);
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
}
