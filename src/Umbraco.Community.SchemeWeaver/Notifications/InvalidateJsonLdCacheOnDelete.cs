using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache when content is deleted. Ripples to descendants only when they
/// actually depend on the deleted node (inherited / cross-node schema) — see
/// <see cref="JsonLdCacheInvalidator"/>.
/// </summary>
public sealed class InvalidateJsonLdCacheOnDelete : INotificationHandler<ContentDeletedNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly ISchemaMappingRepository _mappingRepository;
    private readonly ILogger<InvalidateJsonLdCacheOnDelete> _logger;

    public InvalidateJsonLdCacheOnDelete(
        IJsonLdBlocksProvider provider,
        ISchemaMappingRepository mappingRepository,
        ILogger<InvalidateJsonLdCacheOnDelete> logger)
    {
        _provider = provider;
        _mappingRepository = mappingRepository;
        _logger = logger;
    }

    public void Handle(ContentDeletedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(_provider, _mappingRepository, _logger, notification.DeletedEntities);
}
