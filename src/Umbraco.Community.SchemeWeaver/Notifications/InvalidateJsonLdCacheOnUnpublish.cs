using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache when content is unpublished. Ripples to the whole cache only when
/// other nodes actually depend on the unpublished node (inherited / cross-node schema, or the
/// site-settings node) — see <see cref="JsonLdCacheInvalidator"/>.
/// </summary>
public sealed class InvalidateJsonLdCacheOnUnpublish : INotificationHandler<ContentUnpublishedNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly ISchemaMappingRepository _mappingRepository;
    private readonly SchemeWeaverOptions _options;
    private readonly ILogger<InvalidateJsonLdCacheOnUnpublish> _logger;

    public InvalidateJsonLdCacheOnUnpublish(
        IJsonLdBlocksProvider provider,
        ISchemaMappingRepository mappingRepository,
        IOptions<SchemeWeaverOptions> options,
        ILogger<InvalidateJsonLdCacheOnUnpublish> logger)
    {
        _provider = provider;
        _mappingRepository = mappingRepository;
        _options = options.Value;
        _logger = logger;
    }

    public void Handle(ContentUnpublishedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(
            _provider, _mappingRepository, _logger, notification.UnpublishedEntities, _options.SiteSettings);
}
