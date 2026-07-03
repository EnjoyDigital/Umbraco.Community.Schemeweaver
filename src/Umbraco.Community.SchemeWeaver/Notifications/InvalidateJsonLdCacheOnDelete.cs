using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the JSON-LD cache when content is deleted. Ripples to the whole cache only when other
/// nodes actually depend on the deleted node (inherited / cross-node schema, or the site-settings
/// node) — see <see cref="JsonLdCacheInvalidator"/>.
/// </summary>
public sealed class InvalidateJsonLdCacheOnDelete : INotificationHandler<ContentDeletedNotification>
{
    private readonly IJsonLdBlocksProvider _provider;
    private readonly ISchemaMappingRepository _mappingRepository;
    private readonly SchemeWeaverOptions _options;
    private readonly ILogger<InvalidateJsonLdCacheOnDelete> _logger;

    public InvalidateJsonLdCacheOnDelete(
        IJsonLdBlocksProvider provider,
        ISchemaMappingRepository mappingRepository,
        IOptions<SchemeWeaverOptions> options,
        ILogger<InvalidateJsonLdCacheOnDelete> logger)
    {
        _provider = provider;
        _mappingRepository = mappingRepository;
        _options = options.Value;
        _logger = logger;
    }

    public void Handle(ContentDeletedNotification notification) =>
        JsonLdCacheInvalidator.InvalidateTree(
            _provider, _mappingRepository, _logger, notification.DeletedEntities, _options.SiteSettings);
}
