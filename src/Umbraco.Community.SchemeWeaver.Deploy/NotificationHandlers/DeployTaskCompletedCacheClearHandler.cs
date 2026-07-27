using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Deploy.Core.Events;

namespace Umbraco.Community.SchemeWeaver.Deploy.NotificationHandlers;

/// <summary>
/// Clears the schema-mapping cache after a Deploy work item finishes (or fails).
/// </summary>
/// <remarks>
/// Deploy wraps a whole disk read in one ambient scope, and the repository's cache
/// eviction fires when each nested write scope disposes — BEFORE the outer commit.
/// A concurrent render (or the connector itself, initialising the next artifact) can
/// therefore repopulate the cache with uncommitted rows mid-deployment, and a failed
/// deployment's rollback leaves phantom rows cached. Clearing once per completed
/// task closes both windows; the 5-minute cache backstop remains the fallback.
/// These notifications only exist when the Deploy runtime is installed, so this
/// handler simply never fires without it.
/// </remarks>
public class DeployTaskCompletedCacheClearHandler :
    INotificationAsyncHandler<TaskCompletedNotification>,
    INotificationAsyncHandler<TaskFailedNotification>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeployTaskCompletedCacheClearHandler> _logger;

    public DeployTaskCompletedCacheClearHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<DeployTaskCompletedCacheClearHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task HandleAsync(TaskCompletedNotification notification, CancellationToken cancellationToken)
        => ClearCacheAsync();

    public Task HandleAsync(TaskFailedNotification notification, CancellationToken cancellationToken)
        => ClearCacheAsync();

    private Task ClearCacheAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>().ClearCache();

            // Deploy extraction writes via the repository, so the service-layer
            // notifications that normally evict the JSON-LD output cache
            // (InvalidateJsonLdCacheOnMappingChange) never fire — flush it here or
            // pages keep serving pre-deployment JSON-LD until entries expire.
            scope.ServiceProvider.GetService<IJsonLdBlocksProvider>()?.InvalidateAll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear the schema-mapping cache after a Deploy task.");
        }

        return Task.CompletedTask;
    }
}
