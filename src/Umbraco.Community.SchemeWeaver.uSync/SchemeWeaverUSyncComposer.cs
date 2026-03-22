using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using uSync.Core.Serialization;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Registers SchemeWeaver uSync serializers with the uSync serializer collection.
/// </summary>
public class SchemeWeaverUSyncComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.WithCollectionBuilder<SyncSerializerCollectionBuilder>()
            .Add<SchemaMappingSerializer>();
    }
}
