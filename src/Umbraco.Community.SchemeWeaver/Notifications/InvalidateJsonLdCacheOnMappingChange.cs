using Umbraco.Cms.Core.Events;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Notifications;

/// <summary>
/// Evicts the whole JSON-LD cache when a schema mapping is saved or deleted.
///
/// Without this, the Delivery API JSON-LD endpoint kept serving each page's
/// previously cached graph after a mapping edit until that page was republished
/// (or the entry expired) — the content-lifecycle handlers never fire for
/// mapping changes. A mapping edit can affect every node of the mapped type,
/// its descendants (inherited schemas), and any page whose graph bakes in the
/// affected pieces, so the broad O(1) flush is both correct and cheap for what
/// is a rare, editor-initiated action.
/// </summary>
public sealed class InvalidateJsonLdCacheOnMappingChange :
    INotificationHandler<SchemaMappingSavedNotification>,
    INotificationHandler<SchemaMappingDeletedNotification>
{
    private readonly IJsonLdBlocksProvider _provider;

    public InvalidateJsonLdCacheOnMappingChange(IJsonLdBlocksProvider provider)
    {
        _provider = provider;
    }

    public void Handle(SchemaMappingSavedNotification notification) => _provider.InvalidateAll();

    public void Handle(SchemaMappingDeletedNotification notification) => _provider.InvalidateAll();
}
