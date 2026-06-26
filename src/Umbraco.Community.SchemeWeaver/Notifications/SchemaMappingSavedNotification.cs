using Umbraco.Cms.Core.Notifications;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Published by <see cref="Services.SchemeWeaverService"/> after a schema
/// mapping (and its property mappings) has been persisted. Decouples the main
/// package from optional satellites — the uSync addon subscribes to export the
/// mapping to disk without the core package taking a uSync dependency.
/// Publishing happens at the service layer (not the repository) so the
/// uSync first-boot importer, which writes through the repository directly,
/// can never trigger an import → save → export loop.
/// </summary>
public class SchemaMappingSavedNotification : INotification
{
    public SchemaMappingSavedNotification(string contentTypeAlias, Guid contentTypeKey)
    {
        ContentTypeAlias = contentTypeAlias;
        ContentTypeKey = contentTypeKey;
    }

    public string ContentTypeAlias { get; }

    public Guid ContentTypeKey { get; }
}
