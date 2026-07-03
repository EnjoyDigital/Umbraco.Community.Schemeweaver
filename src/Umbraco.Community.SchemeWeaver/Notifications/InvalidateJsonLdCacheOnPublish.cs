using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache for published content. Ripples to the whole cache only when other
/// nodes actually depend on the published node — an inherited or cross-node schema, or the
/// published node being the site-settings node whose Organization/WebSite pieces are baked into
/// every routed page's cached graph — see <see cref="JsonLdCacheInvalidator"/>. Publishing a leaf
/// article therefore no longer loads the whole content subtree from the DB under the publish
/// write lock.
/// </summary>
public sealed class InvalidateJsonLdCacheOnPublish : INotificationHandler<ContentPublishedNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly ISchemaMappingRepository _mappingRepository;
    private readonly SchemeWeaverOptions _options;
    private readonly ILogger<InvalidateJsonLdCacheOnPublish> _logger;

    public InvalidateJsonLdCacheOnPublish(
        IJsonLdBlocksProvider provider,
        ISchemaMappingRepository mappingRepository,
        IOptions<SchemeWeaverOptions> options,
        ILogger<InvalidateJsonLdCacheOnPublish> logger)
    {
        _provider = provider;
        _mappingRepository = mappingRepository;
        _options = options.Value;
        _logger = logger;
    }

    public void Handle(ContentPublishedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(
            _provider, _mappingRepository, _logger, notification.PublishedEntities, _options.SiteSettings);
}
