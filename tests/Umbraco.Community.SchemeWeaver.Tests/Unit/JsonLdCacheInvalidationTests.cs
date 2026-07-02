using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Each notification handler evicts the affected content's own cache key, then ripples to
/// descendants with a single O(1) <see cref="IJsonLdBlocksProvider.InvalidateAll"/> ONLY when
/// they can depend on it — the published type is inherited, the site uses cross-node mappings,
/// or the event is a move. It no longer walks the subtree from the DB under the publish lock.
/// </summary>
public class JsonLdCacheInvalidationTests
{
    private readonly IJsonLdBlocksProvider _provider = Substitute.For<IJsonLdBlocksProvider>();
    private readonly ISchemaMappingRepository _repo = Substitute.For<ISchemaMappingRepository>();

    public JsonLdCacheInvalidationTests()
    {
        // Default: no dependencies anywhere -> a publish should NOT ripple.
        _repo.GetByContentTypeAlias(Arg.Any<string>()).Returns((SchemaMapping?)null);
        _repo.GetInheritedMappings().Returns(Array.Empty<SchemaMapping>());
        _repo.GetAllPropertyMappingsByMappingId().Returns(new Dictionary<int, List<PropertyMapping>>());
    }

    private static IContent MakeContent(string alias = "article", Guid? key = null)
    {
        var content = Substitute.For<IContent>();
        content.Key.Returns(key ?? Guid.NewGuid());
        var ct = Substitute.For<ISimpleContentType>();
        ct.Alias.Returns(alias);
        content.ContentType.Returns(ct);
        return content;
    }

    [Fact]
    public void Publish_LeafWithNoDependencies_InvalidatesOnlyTarget_NoInvalidateAll()
    {
        var target = MakeContent("article");
        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _repo,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentPublishedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.DidNotReceive().InvalidateAll();
    }

    [Fact]
    public void Publish_InheritedType_RipplesWithInvalidateAll()
    {
        var target = MakeContent("homePage");
        _repo.GetByContentTypeAlias("homePage").Returns(new SchemaMapping { ContentTypeAlias = "homePage", IsInherited = true, IsEnabled = true });

        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _repo,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentPublishedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll();
    }

    [Fact]
    public void Publish_SiteUsesCrossNodeSource_RipplesWithInvalidateAll()
    {
        var target = MakeContent("article");
        // A blogArticle elsewhere pulls Publisher from an ancestor — any publish could affect it.
        _repo.GetAllPropertyMappingsByMappingId().Returns(new Dictionary<int, List<PropertyMapping>>
        {
            [1] = new() { new PropertyMapping { SchemaPropertyName = "Publisher", SourceType = "ancestor" } },
        });

        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _repo,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentPublishedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll();
    }

    [Fact]
    public void Unpublish_InheritedType_RipplesWithInvalidateAll()
    {
        var target = MakeContent("homePage");
        _repo.GetByContentTypeAlias("homePage").Returns(new SchemaMapping { IsInherited = true, IsEnabled = true });

        var handler = new InvalidateJsonLdCacheOnUnpublish(_provider, _repo,
            NullLogger<InvalidateJsonLdCacheOnUnpublish>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentUnpublishedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll();
    }

    [Fact]
    public void Delete_LeafWithNoDependencies_InvalidatesOnlyTarget()
    {
        var target = MakeContent("article");
        var handler = new InvalidateJsonLdCacheOnDelete(_provider, _repo,
            NullLogger<InvalidateJsonLdCacheOnDelete>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentDeletedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.DidNotReceive().InvalidateAll();
    }

    [Fact]
    public void Move_AlwaysRipples_EvenWithNoDependencies()
    {
        var target = MakeContent("article");
        // Use each major's non-obsolete MoveEventInfo overload.
#if UMBRACO18
        var moveInfo = new MoveEventInfo<IContent>(target, "-1,1", (Guid?)null);
#else
        var moveInfo = new MoveEventInfo<IContent>(target, "-1,1", 99);
#endif
        var handler = new InvalidateJsonLdCacheOnMove(_provider, _repo,
            NullLogger<InvalidateJsonLdCacheOnMove>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentMovedNotification(moveInfo, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll(); // moves change ancestry/breadcrumbs regardless
    }

    [Fact]
    public void MoveToRecycleBin_AlwaysRipples()
    {
        var target = MakeContent("article");
        var moveInfo = new MoveToRecycleBinEventInfo<IContent>(target, "-1,1");
        var handler = new InvalidateJsonLdCacheOnMove(_provider, _repo,
            NullLogger<InvalidateJsonLdCacheOnMove>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentMovedToRecycleBinNotification(moveInfo, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll();
    }

    [Fact]
    public void MappingLookupThrows_IsSwallowed_TargetStillEvicted_AndRipplesToBeSafe()
    {
        var target = MakeContent("article");
        _repo.GetByContentTypeAlias("article").Returns(_ => throw new InvalidOperationException("db unavailable"));

        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _repo,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        using var messages = new EventMessages();
        var act = () => handler.Handle(new ContentPublishedNotification(target, messages));

        act.Should().NotThrow();
        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll(); // over-invalidate rather than risk stale JSON-LD
    }
}
