using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Warms the <see cref="ISchemaTypeRegistry"/> at application start so the
/// one-off Schema.NET assembly scan happens off the request thread. Without this
/// the first backoffice request that needs schema types (mappings preview,
/// schema-type search, property listing) blocks while the entire Schema.NET
/// assembly is reflected over.
/// </summary>
public sealed class WarmUpSchemaTypeRegistry : INotificationHandler<UmbracoApplicationStartedNotification>
{
    private readonly ISchemaTypeRegistry _registry;
    private readonly ILogger<WarmUpSchemaTypeRegistry> _logger;

    public WarmUpSchemaTypeRegistry(ISchemaTypeRegistry registry, ILogger<WarmUpSchemaTypeRegistry> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public void Handle(UmbracoApplicationStartedNotification notification) =>
        // Fire-and-forget on a background thread: warming must not delay startup,
        // but it typically completes long before the first user request arrives.
        _ = Task.Run(() =>
        {
            try
            {
                _registry.EnsureInitialised();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SchemeWeaver: failed to warm the Schema.org type registry at startup.");
            }
        });
}
