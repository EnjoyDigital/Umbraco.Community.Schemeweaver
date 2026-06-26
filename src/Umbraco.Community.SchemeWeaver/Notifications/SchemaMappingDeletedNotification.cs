using Umbraco.Cms.Core.Notifications;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Published by <see cref="Services.SchemeWeaverService"/> after a schema
/// mapping is deleted (only when a mapping actually existed). The uSync addon
/// subscribes to remove the corresponding mapping file from disk.
/// </summary>
public class SchemaMappingDeletedNotification : INotification
{
    public SchemaMappingDeletedNotification(string contentTypeAlias, Guid contentTypeKey)
    {
        ContentTypeAlias = contentTypeAlias;
        ContentTypeKey = contentTypeKey;
    }

    public string ContentTypeAlias { get; }

    public Guid ContentTypeKey { get; }
}
