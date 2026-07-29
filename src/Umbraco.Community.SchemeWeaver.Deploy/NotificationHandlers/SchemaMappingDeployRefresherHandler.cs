using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Deploy;
using Umbraco.Cms.Core.Events;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Deploy.Core;
using Umbraco.Deploy.Core.Connectors.ServiceConnectors;
using Umbraco.Deploy.Infrastructure.Disk;

namespace Umbraco.Community.SchemeWeaver.Deploy.NotificationHandlers;

/// <summary>
/// Keeps the Deploy revision folder in sync with backoffice edits: a saved mapping
/// (re)writes its .uda artifact and signature, a deleted mapping removes them.
/// </summary>
/// <remarks>
/// <para>
/// Always-on by design (no config flag, unlike the uSync addon's default-off
/// export-on-save): writing disk artifacts on save is the core contract of a Deploy
/// connector — Deploy's own entity refreshers are unconditional, and a default-off
/// flag would ship a silently broken package (schema deploys, mappings don't).
/// </para>
/// <para>
/// The notifications only fire from the service layer; Deploy extraction writes via
/// the repository, so this handler can never be re-triggered by a deployment.
/// Signatures are written alongside artifacts, mirroring Deploy's own refresher
/// base — without them, environment comparison degrades to always-reprocess.
/// </para>
/// <para>
/// All Deploy services are soft-resolved (satellite-without-OnPrem must never break
/// the site) and every failure is logged and swallowed, per the repo error policy:
/// a Deploy hiccup must never break the user's save.
/// </para>
/// </remarks>
public class SchemaMappingDeployRefresherHandler :
    INotificationAsyncHandler<SchemaMappingSavedNotification>,
    INotificationAsyncHandler<SchemaMappingDeletedNotification>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SchemaMappingDeployRefresherHandler> _logger;

    public SchemaMappingDeployRefresherHandler(
        IServiceProvider serviceProvider,
        ILogger<SchemaMappingDeployRefresherHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task HandleAsync(SchemaMappingSavedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var diskEntityService = ResolveDiskEntityService();
            var connectorFactory = _serviceProvider.GetService<IServiceConnectorFactory>();
            if (diskEntityService is null || connectorFactory is null)
            {
                return;
            }

            if (notification.ContentTypeKey == Guid.Empty)
            {
                _logger.LogWarning(
                    "Not writing a Deploy artifact for schema mapping {Alias}: its ContentTypeKey is empty.",
                    notification.ContentTypeAlias);
                return;
            }

            var udi = new GuidUdi(SchemeWeaverDeployConstants.MappingUdiEntityType, notification.ContentTypeKey);
            var connector = connectorFactory.GetConnector(SchemeWeaverDeployConstants.MappingUdiEntityType);
            var artifact = await connector.GetArtifactAsync(udi, new DictionaryCache(), cancellationToken)
                .ConfigureAwait(false);
            if (artifact is null)
            {
                // Mapping vanished (race with delete) or its content type is gone.
                return;
            }

            await diskEntityService.WriteArtifactsAsync(new[] { artifact }, cancellationToken).ConfigureAwait(false);
            _serviceProvider.GetService<ISignatureService>()?.SetSignatures(new[] { artifact });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to write the Deploy artifact for schema mapping {Alias}.", notification.ContentTypeAlias);
        }
    }

    public Task HandleAsync(SchemaMappingDeletedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var diskEntityService = ResolveDiskEntityService();
            if (diskEntityService is null || notification.ContentTypeKey == Guid.Empty)
            {
                return Task.CompletedTask;
            }

            var udi = new GuidUdi(SchemeWeaverDeployConstants.MappingUdiEntityType, notification.ContentTypeKey);
            diskEntityService.DeleteArtifacts(new Udi[] { udi });
            _serviceProvider.GetService<ISignatureService>()?.ClearSignature(udi);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete the Deploy artifact for schema mapping {Alias}.", notification.ContentTypeAlias);
        }

        return Task.CompletedTask;
    }

    private IDiskEntityService? ResolveDiskEntityService()
    {
        var diskEntityService = _serviceProvider.GetService<IDiskEntityService>();
        if (diskEntityService is null && DeployRuntimeStatus.TryMarkWarned())
        {
            _logger.LogWarning(
                "Umbraco.Community.SchemeWeaver.Deploy is installed but Umbraco Deploy (OnPrem/Cloud) is not — the SchemeWeaver Deploy connector is inactive.");
        }

        return diskEntityService;
    }
}
