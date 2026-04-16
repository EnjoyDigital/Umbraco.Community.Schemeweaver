using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using uSync.Core.Serialization;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Registers SchemeWeaver uSync serializers and the first-boot mapping importer.
/// </summary>
public class SchemeWeaverUSyncComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.WithCollectionBuilder<SyncSerializerCollectionBuilder>()
            .Add<SchemaMappingSerializer>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, SchemaMappingImportNotificationHandler>();
    }
}
