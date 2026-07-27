using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Composing;
using Umbraco.Community.SchemeWeaver.Deploy.NotificationHandlers;
using Umbraco.Community.SchemeWeaver.Notifications;

namespace Umbraco.Community.SchemeWeaver.Deploy.Composing;

/// <summary>
/// Wires the Deploy satellite: UDI type registration and the notification handlers.
/// The service connector itself is discovered by the CMS type scanner
/// (<c>IDiscoverable</c>) — it must not be registered here.
/// </summary>
/// <remarks>
/// Deliberately does NOT touch <c>IMappingDriftReporter</c>/<c>IMappingExporter</c>:
/// those are uSync-owned seams whose null defaults must survive when only the
/// Deploy satellite is installed. This satellite needs no core seam at all — it
/// consumes only the repository and the two mapping notifications.
/// </remarks>
[ComposeAfter(typeof(SchemeWeaverComposer))]
public class SchemeWeaverDeployComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Deploy parses artifact/dependency UDI strings during comparison and
        // extraction; the type must be known before any .uda is read.
        UdiParser.RegisterUdiType(SchemeWeaverDeployConstants.MappingUdiEntityType, UdiType.GuidUdi);

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, SchemeWeaverDeployStartupHandler>();
        builder.AddNotificationAsyncHandler<SchemaMappingSavedNotification, SchemaMappingDeployRefresherHandler>();
        builder.AddNotificationAsyncHandler<SchemaMappingDeletedNotification, SchemaMappingDeployRefresherHandler>();
        builder.AddNotificationAsyncHandler<ContentTypeDeletedNotification, ContentTypeDeletedCleanupHandler>();
        builder.AddNotificationAsyncHandler<Umbraco.Deploy.Core.Events.TaskCompletedNotification, DeployTaskCompletedCacheClearHandler>();
        builder.AddNotificationAsyncHandler<Umbraco.Deploy.Core.Events.TaskFailedNotification, DeployTaskCompletedCacheClearHandler>();
    }
}
