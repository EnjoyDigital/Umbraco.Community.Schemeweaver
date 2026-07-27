using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core.Events;
using Umbraco.Community.SchemeWeaver.Deploy.NotificationHandlers;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Deploy.Core.Events;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Unit;

public class DeployTaskCompletedCacheClearHandlerTests
{
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly Umbraco.Community.SchemeWeaver.Services.IJsonLdBlocksProvider _jsonLdProvider =
        Substitute.For<Umbraco.Community.SchemeWeaver.Services.IJsonLdBlocksProvider>();

    private DeployTaskCompletedCacheClearHandler BuildHandler()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ISchemaMappingRepository)).Returns(_repository);
        serviceProvider.GetService(typeof(Umbraco.Community.SchemeWeaver.Services.IJsonLdBlocksProvider))
            .Returns(_jsonLdProvider);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return new DeployTaskCompletedCacheClearHandler(
            scopeFactory, Substitute.For<ILogger<DeployTaskCompletedCacheClearHandler>>());
    }

    private static TaskCompletedNotification Completed()
        => new(Substitute.For<Umbraco.Deploy.Core.Work.IWorkItem>(), new EventMessages());

    private static TaskFailedNotification Failed()
        => new(Substitute.For<Umbraco.Deploy.Core.Work.IWorkItem>(), new EventMessages());

    [Fact]
    public async Task TaskCompleted_ClearsMappingCache_AndJsonLdOutputCache()
    {
        await BuildHandler().HandleAsync(Completed(), CancellationToken.None);

        _repository.Received(1).ClearCache();
        _jsonLdProvider.Received(1).InvalidateAll();
    }

    [Fact]
    public async Task TaskFailed_ClearsMappingCache()
    {
        await BuildHandler().HandleAsync(Failed(), CancellationToken.None);

        _repository.Received(1).ClearCache();
    }

    [Fact]
    public async Task RepositoryThrows_IsSwallowed()
    {
        _repository.When(r => r.ClearCache()).Throw(new InvalidOperationException("boom"));

        var act = () => BuildHandler().HandleAsync(Completed(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
