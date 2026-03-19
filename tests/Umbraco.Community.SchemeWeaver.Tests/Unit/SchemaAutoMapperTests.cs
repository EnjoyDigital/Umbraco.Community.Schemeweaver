using FluentAssertions;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class SchemaAutoMapperTests
{
    private readonly IContentTypeService _contentTypeService = Substitute.For<IContentTypeService>();
    private readonly ISchemaTypeRegistry _schemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>();
    private readonly SchemaAutoMapper _sut;

    public SchemaAutoMapperTests()
    {
        _sut = new SchemaAutoMapper(_contentTypeService, _schemaTypeRegistry);
    }

    private IContentType CreateContentTypeWithProperties(params string[] propertyAliases)
    {
        var contentType = Substitute.For<IContentType>();
        var propertyTypes = propertyAliases.Select(alias =>
        {
            var pt = Substitute.For<IPropertyType>();
            pt.Alias.Returns(alias);
            return pt;
        }).ToList();
        contentType.PropertyTypes.Returns(propertyTypes);
        return contentType;
    }

    [Fact]
    public void SuggestMappings_ExactMatch_ReturnsConfidence100()
    {
        var contentType = CreateContentTypeWithProperties("headline");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "Headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(100);
        result[0].SuggestedContentTypePropertyAlias.Should().Be("headline");
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_SynonymMatch_ReturnsConfidence80()
    {
        var contentType = CreateContentTypeWithProperties("title");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(80);
        result[0].SuggestedContentTypePropertyAlias.Should().Be("title");
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_PartialMatch_ReturnsConfidence50()
    {
        var contentType = CreateContentTypeWithProperties("blogHeadline");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(50);
        result[0].SuggestedContentTypePropertyAlias.Should().Be("blogHeadline");
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_NoMatch_ReturnsConfidence0()
    {
        var contentType = CreateContentTypeWithProperties("somethingUnrelated");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(0);
        result[0].IsAutoMapped.Should().BeFalse();
        result[0].SuggestedContentTypePropertyAlias.Should().BeNull();
    }

    [Fact]
    public void SuggestMappings_CaseInsensitive_ExactMatch()
    {
        var contentType = CreateContentTypeWithProperties("HEADLINE");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "Headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(100);
    }

    [Fact]
    public void SuggestMappings_UnknownContentType_ReturnsEmpty()
    {
        _contentTypeService.Get("unknown").Returns((IContentType?)null);

        var result = _sut.SuggestMappings("unknown", "Article");

        result.Should().BeEmpty();
    }

    [Fact]
    public void SuggestMappings_MultipleProperties_MappedSimultaneously()
    {
        var contentType = CreateContentTypeWithProperties("headline", "description", "image");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "description", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "image", PropertyType = "URL" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().HaveCount(3);
        result.Should().AllSatisfy(s => s.Confidence.Should().Be(100));
    }

    [Fact]
    public void SuggestMappings_SortedBySchemaProperty_NotConfidence()
    {
        // Verify suggestions are returned in schema property order (one per schema prop)
        var contentType = CreateContentTypeWithProperties("title", "unrelated");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "unknownProp", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().HaveCount(2);
        result[0].SchemaPropertyName.Should().Be("headline");
        result[0].Confidence.Should().Be(80); // synonym match: title -> headline
        result[1].SchemaPropertyName.Should().Be("unknownProp");
        result[1].Confidence.Should().Be(0);
    }
}
