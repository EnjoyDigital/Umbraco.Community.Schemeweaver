using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache when content is moved within the tree (or into the recycle bin).
/// The ancestor chain changes, so inherited schemas + breadcrumb paths on the moved node and
/// every descendant may differ — moves therefore always ripple to descendants
/// (<c>alwaysRippleToDescendants: true</c>), as a cheap O(1) invalidate-all.
/// </summary>
public sealed class InvalidateJsonLdCacheOnMove :
    INotificationHandler<ContentMovedNotification>,
    INotificationHandler<ContentMovedToRecycleBinNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly ISchemaMappingRepository _mappingRepository;
    private readonly SchemeWeaverOptions _options;
    private readonly ILogger<InvalidateJsonLdCacheOnMove> _logger;

    public InvalidateJsonLdCacheOnMove(
        IJsonLdBlocksProvider provider,
        ISchemaMappingRepository mappingRepository,
        IOptions<SchemeWeaverOptions> options,
        ILogger<InvalidateJsonLdCacheOnMove> logger)
    {
        _provider = provider;
        _mappingRepository = mappingRepository;
        _options = options.Value;
        _logger = logger;
    }

    public void Handle(ContentMovedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(
            _provider, _mappingRepository, _logger,
            notification.MoveInfoCollection.Select(m => m.Entity),
            _options.SiteSettings,
            alwaysRippleToDescendants: true);

    public void Handle(ContentMovedToRecycleBinNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(
            _provider, _mappingRepository, _logger,
            notification.MoveInfoCollection.Select(m => m.Entity),
            _options.SiteSettings,
            alwaysRippleToDescendants: true);
}
