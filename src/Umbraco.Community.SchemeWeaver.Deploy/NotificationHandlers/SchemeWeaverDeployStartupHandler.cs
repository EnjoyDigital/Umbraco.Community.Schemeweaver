using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Deploy.Infrastructure.Disk;

namespace Umbraco.Community.SchemeWeaver.Deploy.NotificationHandlers;

/// <summary>
/// Registers the SchemeWeaver mapping entity type as a Deploy disk entity type at
/// startup, so mappings participate in schema export, environment comparison and
/// disk-based deployment alongside document types.
/// </summary>
/// <remarks>
/// <see cref="IDiskEntityService"/> is soft-resolved: it only exists when the licensed
/// Umbraco Deploy OnPrem/Cloud package is installed (this satellite deliberately
/// references only <c>Umbraco.Deploy.Infrastructure</c>). Without it the handler
/// warns once and no-ops — see <see cref="DeployRuntimeStatus"/>.
/// </remarks>
public class SchemeWeaverDeployStartupHandler : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SchemeWeaverDeployStartupHandler> _logger;

    public SchemeWeaverDeployStartupHandler(
        IServiceProvider serviceProvider,
        ILogger<SchemeWeaverDeployStartupHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var diskEntityService = _serviceProvider.GetService<IDiskEntityService>();
            if (diskEntityService is null)
            {
                if (DeployRuntimeStatus.TryMarkWarned())
                {
                    _logger.LogWarning(
                        "Umbraco.Community.SchemeWeaver.Deploy is installed but Umbraco Deploy (OnPrem/Cloud) is not — the SchemeWeaver Deploy connector is inactive.");
                }

                return Task.CompletedTask;
            }

            diskEntityService.RegisterDiskEntityType(SchemeWeaverDeployConstants.MappingUdiEntityType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to register the SchemeWeaver mapping disk entity type with Umbraco Deploy — the connector is inactive.");
        }

        return Task.CompletedTask;
    }
}
