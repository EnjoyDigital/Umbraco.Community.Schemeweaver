using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache for published content. Ripples to descendants only when an inherited
/// or cross-node schema means they actually depend on the published node — see
/// <see cref="JsonLdCacheInvalidator"/>. Publishing a leaf article therefore no longer loads the
/// whole content subtree from the DB under the publish write lock.
/// </summary>
public sealed class InvalidateJsonLdCacheOnPublish : INotificationHandler<ContentPublishedNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly ISchemaMappingRepository _mappingRepository;
    private readonly ILogger<InvalidateJsonLdCacheOnPublish> _logger;

    public InvalidateJsonLdCacheOnPublish(
        IJsonLdBlocksProvider provider,
        ISchemaMappingRepository mappingRepository,
        ILogger<InvalidateJsonLdCacheOnPublish> logger)
    {
        _provider = provider;
        _mappingRepository = mappingRepository;
        _logger = logger;
    }

    public void Handle(ContentPublishedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(_provider, _mappingRepository, _logger, notification.PublishedEntities);
}
