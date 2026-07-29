using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Deploy;
using Umbraco.Community.SchemeWeaver.Deploy.Artifacts;
using Umbraco.Community.SchemeWeaver.Deploy.NotificationHandlers;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Deploy.Core;
using Umbraco.Deploy.Core.Connectors.ServiceConnectors;
using Umbraco.Deploy.Infrastructure.Disk;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Unit;

public class SchemaMappingDeployRefresherHandlerTests : IDisposable
{
    private static readonly Guid Key = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly GuidUdi Udi = new(SchemeWeaverDeployConstants.MappingUdiEntityType, Key);

    private readonly IDiskEntityService _diskEntityService = Substitute.For<IDiskEntityService>();
    private readonly ISignatureService _signatureService = Substitute.For<ISignatureService>();
    private readonly IServiceConnectorFactory _connectorFactory = Substitute.For<IServiceConnectorFactory>();
    private readonly IServiceConnector _connector = Substitute.For<IServiceConnector>();

    public SchemaMappingDeployRefresherHandlerTests()
    {
        DeployRuntimeStatus.Reset();
        _connectorFactory.GetConnector(SchemeWeaverDeployConstants.MappingUdiEntityType).Returns(_connector);
        _connector.GetArtifactAsync(Udi, Arg.Any<IContextCache>(), Arg.Any<CancellationToken>())
            .Returns(new SchemaMappingArtifact(Udi) { ContentTypeAlias = "blogPost" });
    }

    public void Dispose() => DeployRuntimeStatus.Reset();

    private SchemaMappingDeployRefresherHandler BuildHandler(
        bool withDiskService = true, bool withSignatureService = true, bool withConnectorFactory = true)
    {
        var services = new ServiceCollection();
        if (withDiskService)
        {
            services.AddSingleton(_diskEntityService);
        }

        if (withSignatureService)
        {
            services.AddSingleton(_signatureService);
        }

        if (withConnectorFactory)
        {
            services.AddSingleton(_connectorFactory);
        }

        return new SchemaMappingDeployRefresherHandler(
            services.BuildServiceProvider(),
            Substitute.For<ILogger<SchemaMappingDeployRefresherHandler>>());
    }

    private static SchemaMappingSavedNotification Saved(Guid? key = null)
        => new("blogPost", key ?? Key);

    private static SchemaMappingDeletedNotification Deleted(Guid? key = null)
        => new("blogPost", key ?? Key);

    [Fact]
    public async Task Saved_WritesArtifact_AndSetsSignature()
    {
        var handler = BuildHandler();

        await handler.HandleAsync(Saved(), CancellationToken.None);

        await _diskEntityService.Received(1).WriteArtifactsAsync(
            Arg.Is<IEnumerable<IArtifact>>(a => a.Single().Udi.Equals(Udi)), Arg.Any<CancellationToken>());
        _signatureService.Received(1).SetSignatures(
            Arg.Is<IEnumerable<IArtifactSignature>>(s => s.Single().Udi.Equals(Udi)));
    }

    [Fact]
    public async Task Saved_EmptyContentTypeKey_WritesNothing()
    {
        var handler = BuildHandler();

        await handler.HandleAsync(Saved(Guid.Empty), CancellationToken.None);

        await _diskEntityService.DidNotReceiveWithAnyArgs()
            .WriteArtifactsAsync(default(IEnumerable<IArtifact>)!, default);
    }

    [Fact]
    public async Task Saved_MappingVanished_WritesNothing()
    {
        _connector.GetArtifactAsync(Udi, Arg.Any<IContextCache>(), Arg.Any<CancellationToken>())
            .Returns((IArtifact?)null);
        var handler = BuildHandler();

        await handler.HandleAsync(Saved(), CancellationToken.None);

        await _diskEntityService.DidNotReceiveWithAnyArgs()
            .WriteArtifactsAsync(default(IEnumerable<IArtifact>)!, default);
    }

    [Fact]
    public async Task Saved_WithoutDeployRuntime_NoOps()
    {
        var handler = BuildHandler(withDiskService: false, withConnectorFactory: false);

        var act = () => handler.HandleAsync(Saved(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _signatureService.DidNotReceiveWithAnyArgs().SetSignatures(default!);
    }

    [Fact]
    public async Task Saved_DiskServiceThrows_IsSwallowed()
    {
        _diskEntityService.WriteArtifactsAsync(Arg.Any<IEnumerable<IArtifact>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk full"));
        var handler = BuildHandler();

        var act = () => handler.HandleAsync(Saved(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Saved_WithoutSignatureService_StillWritesArtifact()
    {
        var handler = BuildHandler(withSignatureService: false);

        await handler.HandleAsync(Saved(), CancellationToken.None);

        await _diskEntityService.Received(1).WriteArtifactsAsync(
            Arg.Any<IEnumerable<IArtifact>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deleted_DeletesArtifact_AndClearsSignature()
    {
        var handler = BuildHandler();

        await handler.HandleAsync(Deleted(), CancellationToken.None);

        // The Udi-based helper is an extension method that wraps each UDI in a
        // delete-stub artifact before calling the instance method we substitute.
        _diskEntityService.Received(1).DeleteArtifacts(
            Arg.Is<IEnumerable<IArtifact>>(a => a.Single().Udi.Equals(Udi)));
        _signatureService.Received(1).ClearSignature(Udi);
    }

    [Fact]
    public async Task Deleted_EmptyContentTypeKey_DeletesNothing()
    {
        var handler = BuildHandler();

        await handler.HandleAsync(Deleted(Guid.Empty), CancellationToken.None);

        _diskEntityService.DidNotReceiveWithAnyArgs().DeleteArtifacts(default(IEnumerable<IArtifact>)!);
    }

    [Fact]
    public async Task Deleted_WithoutDeployRuntime_NoOps()
    {
        var handler = BuildHandler(withDiskService: false, withConnectorFactory: false);

        var act = () => handler.HandleAsync(Deleted(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Deleted_DiskServiceThrows_IsSwallowed()
    {
        _diskEntityService.When(d => d.DeleteArtifacts(Arg.Any<IEnumerable<IArtifact>>()))
            .Throw(new IOException("locked"));
        var handler = BuildHandler();

        var act = () => handler.HandleAsync(Deleted(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void WarnOnceLatch_FiresExactlyOnce()
    {
        DeployRuntimeStatus.Reset();

        DeployRuntimeStatus.TryMarkWarned().Should().BeTrue();
        DeployRuntimeStatus.TryMarkWarned().Should().BeFalse();

        DeployRuntimeStatus.Reset();
        DeployRuntimeStatus.TryMarkWarned().Should().BeTrue();
    }
}
