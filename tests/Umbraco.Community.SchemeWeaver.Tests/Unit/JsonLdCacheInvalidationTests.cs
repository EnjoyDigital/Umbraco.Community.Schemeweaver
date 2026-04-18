using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Each notification handler must evict the target content plus all descendants, because
/// inherited schemas on an ancestor propagate to every descendant's block array.
/// </summary>
public class JsonLdCacheInvalidationTests
{
    private readonly IJsonLdBlocksProvider _provider = Substitute.For<IJsonLdBlocksProvider>();
    private readonly IContentService _contentService = Substitute.For<IContentService>();

    private static IContent MakeContent(int id, Guid? key = null)
    {
        var content = Substitute.For<IContent>();
        content.Id.Returns(id);
        content.Key.Returns(key ?? Guid.NewGuid());
        return content;
    }

    private void WireDescendants(int parentId, params IContent[] descendants)
    {
        long total = descendants.Length;
        _contentService
            .GetPagedDescendants(
                parentId,
                Arg.Any<long>(),
                Arg.Any<int>(),
                out Arg.Any<long>(),
                Arg.Any<IQuery<IContent>?>(),
                Arg.Any<Ordering?>())
            .Returns(call =>
            {
                call[3] = total;
                return descendants;
            });
    }

    [Fact]
    public void Publish_Invalidates_TargetPlusDescendants()
    {
        var target = MakeContent(id: 1);
        var childA = MakeContent(id: 2);
        var childB = MakeContent(id: 3);
        WireDescendants(target.Id, childA, childB);

        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _contentService,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        handler.Handle(new ContentPublishedNotification(target, new EventMessages()));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).Invalidate(childA.Key);
        _provider.Received(1).Invalidate(childB.Key);
    }

    [Fact]
    public void Unpublish_Invalidates_TargetPlusDescendants()
    {
        var target = MakeContent(id: 1);
        var child = MakeContent(id: 2);
        WireDescendants(target.Id, child);

        var handler = new InvalidateJsonLdCacheOnUnpublish(_provider, _contentService,
            NullLogger<InvalidateJsonLdCacheOnUnpublish>.Instance);

        handler.Handle(new ContentUnpublishedNotification(target, new EventMessages()));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).Invalidate(child.Key);
    }

    [Fact]
    public void Move_Invalidates_TargetPlusDescendants()
    {
        var target = MakeContent(id: 1);
        var child = MakeContent(id: 2);
        WireDescendants(target.Id, child);
        var moveInfo = new MoveEventInfo<IContent>(target, "-1,1", 99, null);

        var handler = new InvalidateJsonLdCacheOnMove(_provider, _contentService,
            NullLogger<InvalidateJsonLdCacheOnMove>.Instance);

        handler.Handle(new ContentMovedNotification(moveInfo, new EventMessages()));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).Invalidate(child.Key);
    }

    [Fact]
    public void MoveToRecycleBin_Invalidates_TargetPlusDescendants()
    {
        var target = MakeContent(id: 1);
        var child = MakeContent(id: 2);
        WireDescendants(target.Id, child);
        var moveInfo = new MoveToRecycleBinEventInfo<IContent>(target, "-1,1");

        var handler = new InvalidateJsonLdCacheOnMove(_provider, _contentService,
            NullLogger<InvalidateJsonLdCacheOnMove>.Instance);

        handler.Handle(new ContentMovedToRecycleBinNotification(moveInfo, new EventMessages()));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).Invalidate(child.Key);
    }

    [Fact]
    public void Delete_Invalidates_TargetPlusDescendants()
    {
        var target = MakeContent(id: 1);
        var child = MakeContent(id: 2);
        WireDescendants(target.Id, child);

        var handler = new InvalidateJsonLdCacheOnDelete(_provider, _contentService,
            NullLogger<InvalidateJsonLdCacheOnDelete>.Instance);

        handler.Handle(new ContentDeletedNotification(target, new EventMessages()));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).Invalidate(child.Key);
    }

    [Fact]
    public void DescendantWalkThrows_IsSwallowed_TargetStillEvicted()
    {
        var target = MakeContent(id: 1);
        _contentService
            .GetPagedDescendants(
                target.Id,
                Arg.Any<long>(),
                Arg.Any<int>(),
                out Arg.Any<long>(),
                Arg.Any<IQuery<IContent>?>(),
                Arg.Any<Ordering?>())
            .Returns(_ => throw new InvalidOperationException("db unavailable"));

        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _contentService,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        var act = () => handler.Handle(new ContentPublishedNotification(target, new EventMessages()));

        act.Should().NotThrow();
        _provider.Received(1).Invalidate(target.Key);
    }
}
