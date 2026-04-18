using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache when content is deleted. Descendants are included for the same
/// reason as publish — removing an ancestor changes every inherited block chain below it.
/// </summary>
public sealed class InvalidateJsonLdCacheOnDelete : INotificationHandler<ContentDeletedNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly IContentService _contentService;
    private readonly ILogger<InvalidateJsonLdCacheOnDelete> _logger;

    public InvalidateJsonLdCacheOnDelete(
        IJsonLdBlocksProvider provider,
        IContentService contentService,
        ILogger<InvalidateJsonLdCacheOnDelete> logger)
    {
        _provider = provider;
        _contentService = contentService;
        _logger = logger;
    }

    public void Handle(ContentDeletedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(_provider, _contentService, _logger, notification.DeletedEntities);
}
