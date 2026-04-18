using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache when content is moved within the tree (or into the recycle bin).
/// The ancestor chain changes, so inherited schemas + breadcrumb paths on the moved node and
/// every descendant may differ from their cached values.
/// </summary>
public sealed class InvalidateJsonLdCacheOnMove :
    INotificationHandler<ContentMovedNotification>,
    INotificationHandler<ContentMovedToRecycleBinNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly IContentService _contentService;
    private readonly ILogger<InvalidateJsonLdCacheOnMove> _logger;

    public InvalidateJsonLdCacheOnMove(
        IJsonLdBlocksProvider provider,
        IContentService contentService,
        ILogger<InvalidateJsonLdCacheOnMove> logger)
    {
        _provider = provider;
        _contentService = contentService;
        _logger = logger;
    }

    public void Handle(ContentMovedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(
            _provider, _contentService, _logger,
            notification.MoveInfoCollection.Select(m => m.Entity));

    public void Handle(ContentMovedToRecycleBinNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(
            _provider, _contentService, _logger,
            notification.MoveInfoCollection.Select(m => m.Entity));
}
