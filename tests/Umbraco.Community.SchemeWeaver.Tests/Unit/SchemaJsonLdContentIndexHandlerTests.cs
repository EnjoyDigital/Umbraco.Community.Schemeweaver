using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.SchemeWeaver.DeliveryApi;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// The handler now delegates all schema-block generation to
/// <see cref="IJsonLdBlocksProvider"/> (see <see cref="JsonLdBlocksProviderTests"/> for the
/// ordering / opt-out coverage). These tests focus on the Examine-index surface: how the
/// handler responds to missing context, absent published content, and empty provider output.
/// </summary>
public class SchemaJsonLdContentIndexHandlerTests
{
    private readonly IUmbracoContextAccessor _umbracoContextAccessor = Substitute.For<IUmbracoContextAccessor>();
    private readonly IJsonLdBlocksProvider _blocksProvider = Substitute.For<IJsonLdBlocksProvider>();

    private SchemaJsonLdContentIndexHandler CreateHandler() =>
        new(_umbracoContextAccessor, _blocksProvider, NullLogger<SchemaJsonLdContentIndexHandler>.Instance);

    private (IContent content, IPublishedContent published) WireContent(Guid? key = null)
    {
        var contentKey = key ?? Guid.NewGuid();
        var content = Substitute.For<IContent>();
        content.Key.Returns(contentKey);

        var published = Substitute.For<IPublishedContent>();
        var contentCache = Substitute.For<IPublishedContentCache>();
        contentCache.GetById(contentKey).Returns(published);

        var umbracoContext = Substitute.For<IUmbracoContext>();
        umbracoContext.Content.Returns(contentCache);

        _umbracoContextAccessor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext?>())
            .Returns(call =>
            {
                call[0] = umbracoContext;
                return true;
            });

        return (content, published);
    }

    [Fact]
    public void GetFieldValues_DelegatesToProviderAndYieldsSchemaOrgField()
    {
        var handler = CreateHandler();
        var (content, published) = WireContent();
        _blocksProvider.GetBlocks(published, "en-gb").Returns(["{\"@type\":\"WebSite\"}"]);

        var result = handler.GetFieldValues(content, "en-gb").ToList();

        result.Should().HaveCount(1);
        result[0].FieldName.Should().Be("schemaOrg");
        result[0].Values.Should().ContainSingle().Which.Should().Be("{\"@type\":\"WebSite\"}");
    }

    [Fact]
    public void GetFieldValues_EmptyProviderOutput_YieldsNothing()
    {
        var handler = CreateHandler();
        var (content, published) = WireContent();
        _blocksProvider.GetBlocks(published, Arg.Any<string?>()).Returns([]);

        var result = handler.GetFieldValues(content, culture: null).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetFieldValues_NoPublishedContent_YieldsNothing()
    {
        var handler = CreateHandler();
        var content = Substitute.For<IContent>();
        content.Key.Returns(Guid.NewGuid());
        var contentCache = Substitute.For<IPublishedContentCache>();
        contentCache.GetById(Arg.Any<Guid>()).Returns((IPublishedContent?)null);
        var umbracoContext = Substitute.For<IUmbracoContext>();
        umbracoContext.Content.Returns(contentCache);
        _umbracoContextAccessor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext?>())
            .Returns(call =>
            {
                call[0] = umbracoContext;
                return true;
            });

        var result = handler.GetFieldValues(content, culture: null).ToList();

        result.Should().BeEmpty();
        _blocksProvider.DidNotReceiveWithAnyArgs().GetBlocks(default!, default);
    }

    [Fact]
    public void GetFieldValues_NoUmbracoContext_YieldsNothing()
    {
        var handler = CreateHandler();
        _umbracoContextAccessor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext?>()).Returns(false);
        var content = Substitute.For<IContent>();
        content.Key.Returns(Guid.NewGuid());

        var result = handler.GetFieldValues(content, culture: null).ToList();

        result.Should().BeEmpty();
    }
}
