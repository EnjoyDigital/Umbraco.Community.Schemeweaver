using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache for published content and every descendant — inherited schemas
/// ripple down the tree, so publishing Home must invalidate every page that inherits its
/// <c>WebSite</c> schema.
/// </summary>
public sealed class InvalidateJsonLdCacheOnPublish : INotificationHandler<ContentPublishedNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly IContentService _contentService;
    private readonly ILogger<InvalidateJsonLdCacheOnPublish> _logger;

    public InvalidateJsonLdCacheOnPublish(
        IJsonLdBlocksProvider provider,
        IContentService contentService,
        ILogger<InvalidateJsonLdCacheOnPublish> logger)
    {
        _provider = provider;
        _contentService = contentService;
        _logger = logger;
    }

    public void Handle(ContentPublishedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(_provider, _contentService, _logger, notification.PublishedEntities);
}
