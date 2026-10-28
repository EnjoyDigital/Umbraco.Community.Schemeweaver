using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Seo.Overrides;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Seo;

public class SeoContentMappingOverrideProviderTests
{
    private readonly ISeoPageOverrideRepository _repository = Substitute.For<ISeoPageOverrideRepository>();
    private readonly SeoContentMappingOverrideProvider _sut;
    private readonly IPublishedContent _content = Substitute.For<IPublishedContent>();
    private readonly Guid _key = Guid.NewGuid();
    private readonly SchemaMapping _mapping = new() { Id = 1, ContentTypeAlias = "blogPost", SchemaTypeName = "Article", IsEnabled = true };

    public SeoContentMappingOverrideProviderTests()
    {
        _content.Key.Returns(_key);
        _sut = new SeoContentMappingOverrideProvider(
            _repository, SeoTestSupport.Registry, new SchemaRangeChecker(SeoTestSupport.Registry),
            NullLogger<SeoContentMappingOverrideProvider>.Instance);
    }

    [Fact]
    public void NoRow_None() =>
        _sut.Resolve(_content, _mapping, null).Kind.Should().Be(MappingOverrideKind.None);

    [Fact]
    public void Excluded_Excluded()
    {
        _repository.Get(_key).Returns(new SeoPageOverride { ContentKey = _key, IsExcluded = true, SchemaTypeName = "NewsArticle" });
        _sut.Resolve(_content, _mapping, null).Kind.Should().Be(MappingOverrideKind.Excluded);
    }

    [Fact]
    public void Subtype_Retyped_AsAClone()
    {
        _repository.Get(_key).Returns(new SeoPageOverride { ContentKey = _key, SchemaTypeName = "NewsArticle" });
        var result = _sut.Resolve(_content, _mapping, "en-GB");
        result.Kind.Should().Be(MappingOverrideKind.Retyped);
        result.Mapping!.SchemaTypeName.Should().Be("NewsArticle");
        result.Mapping.Id.Should().Be(1);
        _mapping.SchemaTypeName.Should().Be("Article");
    }

    [Theory]
    [InlineData("Article")]      // same type
    [InlineData("Recipe")]       // not a subtype
    [InlineData("NotAType")]     // unknown
    [InlineData("  ")]
    public void NotASubtypeOrNoChange_None(string type)
    {
        _repository.Get(_key).Returns(new SeoPageOverride { ContentKey = _key, SchemaTypeName = type });
        _sut.Resolve(_content, _mapping, null).Kind.Should().Be(MappingOverrideKind.None);
    }

    [Fact]
    public void RepositoryThrows_None()
    {
        _repository.Get(Arg.Any<Guid>()).Returns(_ => throw new InvalidOperationException("db down"));
        _sut.Resolve(_content, _mapping, null).Kind.Should().Be(MappingOverrideKind.None);
    }
}
