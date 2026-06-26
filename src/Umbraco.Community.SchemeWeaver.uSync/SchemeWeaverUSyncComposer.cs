using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Notifications;
using uSync.Core.Serialization;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Registers SchemeWeaver uSync serializers, the first-boot mapping importer,
/// and the export-on-save handler.
/// </summary>
public class SchemeWeaverUSyncComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.WithCollectionBuilder<SyncSerializerCollectionBuilder>()
            .Add<SchemaMappingSerializer>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, SchemaMappingImportNotificationHandler>();

        // Export-on-save: writes a mapping back to its uSync data folder when it
        // is saved/deleted, gated behind the SchemeWeaver-owned default-off flag.
        builder.Services.AddSingleton<IMappingFileWriter, MappingFileWriter>();
        builder.AddNotificationHandler<SchemaMappingSavedNotification, SchemaMappingExportNotificationHandler>();
        builder.AddNotificationHandler<SchemaMappingDeletedNotification, SchemaMappingExportNotificationHandler>();
    }
}
