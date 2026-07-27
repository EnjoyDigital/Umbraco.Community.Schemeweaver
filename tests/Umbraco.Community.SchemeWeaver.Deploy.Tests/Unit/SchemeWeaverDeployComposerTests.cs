using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Deploy.Composing;
using Umbraco.Community.SchemeWeaver.Deploy.NotificationHandlers;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Unit;

public class SchemeWeaverDeployComposerTests
{
    private static IServiceCollection Compose()
    {
        var services = new ServiceCollection();
        var builder = new UmbracoBuilder(
            services,
            new ConfigurationBuilder().Build(),
            new TypeLoader(Substitute.For<ITypeFinder>(), Substitute.For<ILogger<TypeLoader>>()));

        new SchemeWeaverDeployComposer().Compose(builder);
        return services;
    }

    [Fact]
    public void RegistersMappingUdiType_AsClosedGuidUdi()
    {
        Compose();

        var udi = UdiParser.Parse($"umb://schemeweaver-mapping/{Guid.NewGuid():N}");

        udi.Should().BeOfType<GuidUdi>();
    }

    [Fact]
    public void RegistersAllNotificationHandlers()
    {
        var services = Compose();

        services.Should().Contain(d =>
            d.ServiceType == typeof(INotificationAsyncHandler<UmbracoApplicationStartingNotification>) &&
            d.ImplementationType == typeof(SchemeWeaverDeployStartupHandler));
        services.Should().Contain(d =>
            d.ServiceType == typeof(INotificationAsyncHandler<SchemaMappingSavedNotification>) &&
            d.ImplementationType == typeof(SchemaMappingDeployRefresherHandler));
        services.Should().Contain(d =>
            d.ServiceType == typeof(INotificationAsyncHandler<SchemaMappingDeletedNotification>) &&
            d.ImplementationType == typeof(SchemaMappingDeployRefresherHandler));
        services.Should().Contain(d =>
            d.ServiceType == typeof(INotificationAsyncHandler<ContentTypeDeletedNotification>) &&
            d.ImplementationType == typeof(ContentTypeDeletedCleanupHandler));
        services.Should().Contain(d =>
            d.ServiceType == typeof(INotificationAsyncHandler<Umbraco.Deploy.Core.Events.TaskCompletedNotification>) &&
            d.ImplementationType == typeof(DeployTaskCompletedCacheClearHandler));
        services.Should().Contain(d =>
            d.ServiceType == typeof(INotificationAsyncHandler<Umbraco.Deploy.Core.Events.TaskFailedNotification>) &&
            d.ImplementationType == typeof(DeployTaskCompletedCacheClearHandler));
    }

    [Fact]
    public void DoesNotTouchUSyncSeams()
    {
        // IMappingDriftReporter/IMappingExporter are uSync-owned: core registers null
        // defaults and only the uSync satellite may override them. The Deploy
        // composer must leave them alone so "Deploy satellite only" keeps the null
        // implementations resolvable.
        var services = Compose();

        services.Should().NotContain(d => d.ServiceType == typeof(IMappingDriftReporter));
        services.Should().NotContain(d => d.ServiceType == typeof(IMappingExporter));
    }
}
