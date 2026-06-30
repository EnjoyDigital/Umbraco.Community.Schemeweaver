using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache when content is unpublished. Ripples to descendants only when they
/// actually depend on the unpublished node (inherited / cross-node schema) — see
/// <see cref="JsonLdCacheInvalidator"/>.
/// </summary>
public sealed class InvalidateJsonLdCacheOnUnpublish : INotificationHandler<ContentUnpublishedNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly ISchemaMappingRepository _mappingRepository;
    private readonly ILogger<InvalidateJsonLdCacheOnUnpublish> _logger;

    public InvalidateJsonLdCacheOnUnpublish(
        IJsonLdBlocksProvider provider,
        ISchemaMappingRepository mappingRepository,
        ILogger<InvalidateJsonLdCacheOnUnpublish> logger)
    {
        _provider = provider;
        _mappingRepository = mappingRepository;
        _logger = logger;
    }

    public void Handle(ContentUnpublishedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(_provider, _mappingRepository, _logger, notification.UnpublishedEntities);
}
