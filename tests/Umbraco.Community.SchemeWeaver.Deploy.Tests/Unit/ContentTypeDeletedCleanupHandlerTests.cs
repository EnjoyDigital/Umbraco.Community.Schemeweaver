using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Deploy;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Deploy.NotificationHandlers;
using Umbraco.Deploy.Core;
using Umbraco.Deploy.Infrastructure.Disk;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Unit;

public class ContentTypeDeletedCleanupHandlerTests
{
    private static readonly Guid Key = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly IDiskEntityService _diskEntityService = Substitute.For<IDiskEntityService>();
    private readonly ISignatureService _signatureService = Substitute.For<ISignatureService>();

    private ContentTypeDeletedCleanupHandler BuildHandler(bool withDiskService = true)
    {
        var services = new ServiceCollection();
        if (withDiskService)
        {
            services.AddSingleton(_diskEntityService);
            services.AddSingleton(_signatureService);
        }

        return new ContentTypeDeletedCleanupHandler(
            services.BuildServiceProvider(),
            Substitute.For<ILogger<ContentTypeDeletedCleanupHandler>>());
    }

    private static ContentTypeDeletedNotification Notification(params Guid[] keys)
    {
        var contentTypes = keys.Select(k =>
        {
            var contentType = Substitute.For<IContentType>();
            contentType.Key.Returns(k);
            return contentType;
        }).ToArray();

        var notification = new ContentTypeDeletedNotification(contentTypes[0], new EventMessages());
        return contentTypes.Length == 1
            ? notification
            : new ContentTypeDeletedNotification(contentTypes, new EventMessages());
    }

    [Fact]
    public async Task DeletedContentType_RemovesMappingArtifact_AndSignature()
    {
        var handler = BuildHandler();
        var expectedUdi = new GuidUdi(SchemeWeaverDeployConstants.MappingUdiEntityType, Key);

        await handler.HandleAsync(Notification(Key), CancellationToken.None);

        _diskEntityService.Received(1).DeleteArtifacts(
            Arg.Is<IEnumerable<IArtifact>>(a => a.Single().Udi.Equals(expectedUdi)));
        _signatureService.Received(1).ClearSignature(expectedUdi);
    }

    [Fact]
    public async Task MultipleDeletedContentTypes_EachGetArtifactCleanup()
    {
        var handler = BuildHandler();
        var otherKey = Guid.NewGuid();

        await handler.HandleAsync(Notification(Key, otherKey), CancellationToken.None);

        _diskEntityService.Received(2).DeleteArtifacts(Arg.Any<IEnumerable<IArtifact>>());
        _signatureService.Received(1).ClearSignature(
            new GuidUdi(SchemeWeaverDeployConstants.MappingUdiEntityType, Key));
        _signatureService.Received(1).ClearSignature(
            new GuidUdi(SchemeWeaverDeployConstants.MappingUdiEntityType, otherKey));
    }

    [Fact]
    public async Task WithoutDeployRuntime_NoOps()
    {
        var handler = BuildHandler(withDiskService: false);

        var act = () => handler.HandleAsync(Notification(Key), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DiskServiceThrows_IsSwallowed()
    {
        _diskEntityService.When(d => d.DeleteArtifacts(Arg.Any<IEnumerable<IArtifact>>()))
            .Throw(new IOException("locked"));
        var handler = BuildHandler();

        var act = () => handler.HandleAsync(Notification(Key), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
