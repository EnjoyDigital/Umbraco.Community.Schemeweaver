using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Composing;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Services;
using uSync.Core.Serialization;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Registers SchemeWeaver uSync serializers, the dashboard handler, the boot-time mapping
/// importer, the export-on-save handler, and the real drift/export seams. Composes after the
/// core composer so its seam registrations replace the core null defaults.
/// </summary>
[ComposeAfter(typeof(SchemeWeaverComposer))]
public class SchemeWeaverUSyncComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.WithCollectionBuilder<SyncSerializerCollectionBuilder>()
            .Add<SchemaMappingSerializer>();

        // The dashboard handler (SchemaMappingHandler) is intentionally NOT added
        // to the SyncHandlerCollectionBuilder here: uSync discovers every
        // ISyncHandler automatically through its type loader
        // (TypeLoader.GetTypes<ISyncHandler>()), so an explicit Add would register
        // — and draw — the handler twice. It surfaces SchemeWeaver mappings in the
        // uSync dashboard and powers Import All / Export All.

        // First-boot seeding is kept alongside the dashboard handler on purpose:
        // uSync's own startup import only runs when the global
        // Settings:ImportAtStartup flag is enabled (off by default), so it does
        // not reliably seed the mappings a package ships in its uSync folder on a
        // fresh boot. The importer below always runs, but it is idempotent — it
        // skips when any mapping already exists and wraps its work in the import
        // guard — so it can never double-import or fight the dashboard handler.
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, SchemaMappingImportNotificationHandler>();

        // Export-on-save: writes a mapping back to its uSync data folder when it
        // is saved/deleted, gated behind the SchemeWeaver-owned default-off flag.
        builder.Services.AddSingleton<IMappingFileWriter, MappingFileWriter>();
        builder.AddNotificationHandler<SchemaMappingSavedNotification, SchemaMappingExportNotificationHandler>();
        builder.AddNotificationHandler<SchemaMappingDeletedNotification, SchemaMappingExportNotificationHandler>();

        // Real config-as-code seams. Registered after the core null defaults (this composer
        // is [ComposeAfter] the core one), so last-registration-wins resolves these.
        builder.Services.AddScoped<IMappingDriftReporter, USyncMappingDriftReporter>();
        builder.Services.AddScoped<IMappingExporter, USyncMappingExporter>();
    }
}
