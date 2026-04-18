using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache when content is unpublished. Descendants are also evicted — an
/// unpublished ancestor removes itself from the inherited chain, so child pages' inherited
/// block arrays change.
/// </summary>
public sealed class InvalidateJsonLdCacheOnUnpublish : INotificationHandler<ContentUnpublishedNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly IContentService _contentService;
    private readonly ILogger<InvalidateJsonLdCacheOnUnpublish> _logger;

    public InvalidateJsonLdCacheOnUnpublish(
        IJsonLdBlocksProvider provider,
        IContentService contentService,
        ILogger<InvalidateJsonLdCacheOnUnpublish> logger)
    {
        _provider = provider;
        _contentService = contentService;
        _logger = logger;
    }

    public void Handle(ContentUnpublishedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(_provider, _contentService, _logger, notification.UnpublishedEntities);
}
