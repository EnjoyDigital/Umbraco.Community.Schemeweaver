using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Seo.Overrides;
using Umbraco.Community.SchemeWeaver.Seo.Services;

namespace Umbraco.Community.SchemeWeaver.Seo.Notifications;

/// <summary>Deleting content removes its page override.</summary>
public sealed class RemovePageOverridesOnContentDelete : INotificationHandler<ContentDeletedNotification>
{
    private readonly ISeoPageOverrideRepository _repository;

    public RemovePageOverridesOnContentDelete(ISeoPageOverrideRepository repository)
    {
        _repository = repository;
    }

    public void Handle(ContentDeletedNotification notification) =>
        _repository.DeleteMany(notification.DeletedEntities.Select(c => c.Key));
}

/// <summary>A mapping change invalidates the cached site health report.</summary>
public sealed class EvictSeoHealthReportOnMappingChange :
    INotificationHandler<SchemaMappingSavedNotification>,
    INotificationHandler<SchemaMappingDeletedNotification>
{
    private readonly ISeoHealthCheckService _health;

    public EvictSeoHealthReportOnMappingChange(ISeoHealthCheckService health)
    {
        _health = health;
    }

    public void Handle(SchemaMappingSavedNotification notification) => _health.Evict();

    public void Handle(SchemaMappingDeletedNotification notification) => _health.Evict();
}
