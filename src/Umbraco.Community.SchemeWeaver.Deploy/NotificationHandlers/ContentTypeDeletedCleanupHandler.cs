using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Deploy.Core;
using Umbraco.Deploy.Infrastructure.Disk;

namespace Umbraco.Community.SchemeWeaver.Deploy.NotificationHandlers;

/// <summary>
/// Removes a mapping's .uda artifact (and signature) when its content type is
/// deleted. Core keeps the orphaned mapping row (existing behaviour), but the
/// artifact must go: a mapping .uda whose document-type dependency can never be
/// satisfied again is a hard Error during every subsequent schema deployment on
/// the target, until someone hand-deletes the file from source control.
/// </summary>
public class ContentTypeDeletedCleanupHandler : INotificationAsyncHandler<ContentTypeDeletedNotification>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContentTypeDeletedCleanupHandler> _logger;

    public ContentTypeDeletedCleanupHandler(
        IServiceProvider serviceProvider,
        ILogger<ContentTypeDeletedCleanupHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleAsync(ContentTypeDeletedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var diskEntityService = _serviceProvider.GetService<IDiskEntityService>();
            if (diskEntityService is null)
            {
                return Task.CompletedTask;
            }

            var signatureService = _serviceProvider.GetService<ISignatureService>();

            // Deleting a .uda that never existed is a no-op, so no repository probe
            // is needed to check whether the doc type actually had a mapping.
            foreach (var contentType in notification.DeletedEntities)
            {
                var udi = new GuidUdi(SchemeWeaverDeployConstants.MappingUdiEntityType, contentType.Key);
                diskEntityService.DeleteArtifacts(new Udi[] { udi });
                signatureService?.ClearSignature(udi);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to clean up SchemeWeaver Deploy artifacts after content type deletion.");
        }

        return Task.CompletedTask;
    }
}
