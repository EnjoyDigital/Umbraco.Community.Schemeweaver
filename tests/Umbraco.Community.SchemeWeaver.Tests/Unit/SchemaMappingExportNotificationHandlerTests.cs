using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Community.SchemeWeaver;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.uSync;
using uSync.Core.Serialization;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Unit-tests the GATING of the uSync export-on-save handler only. The full
/// serialise → disk round-trip is an integration concern (deferred).
/// </summary>
public class SchemaMappingExportNotificationHandlerTests
{
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly IMappingFileWriter _fileWriter = Substitute.For<IMappingFileWriter>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly SchemeWeaverOptions _options = new();

    public SchemaMappingExportNotificationHandlerTests()
    {
        _hostEnvironment.ContentRootPath.Returns(Path.GetTempPath());

        _repository.GetByContentTypeAlias("article").Returns(new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            ContentTypeKey = Guid.NewGuid(),
            SchemaTypeName = "Article",
            IsEnabled = true
        });
        _repository.GetPropertyMappings(1).Returns([]);
    }

    private SchemaMappingExportNotificationHandler CreateHandler()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ISchemaMappingRepository)).Returns(_repository);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var serializer = new SchemaMappingSerializer(
            scopeFactory, Substitute.For<ILogger<SchemaMappingSerializer>>());
        var serializers = new SyncSerializerCollection(() => new[] { serializer });

        return new SchemaMappingExportNotificationHandler(
            serializers,
            scopeFactory,
            _hostEnvironment,
            _fileWriter,
            Options.Create(_options),
            Substitute.For<ILogger<SchemaMappingExportNotificationHandler>>());
    }

    [Fact]
    public void Save_FlagOff_DoesNotWrite()
    {
        _options.ExportMappingsToUSyncOnSave = false;
        var handler = CreateHandler();

        handler.Handle(new SchemaMappingSavedNotification("article", Guid.NewGuid()));

        _fileWriter.DidNotReceive().Write(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<XElement>());
    }

    [Fact]
    public void Save_ImportGuardSet_DoesNotWrite()
    {
        _options.ExportMappingsToUSyncOnSave = true;
        var handler = CreateHandler();

        using (SchemeWeaverImportGuard.Enter())
        {
            handler.Handle(new SchemaMappingSavedNotification("article", Guid.NewGuid()));
        }

        _fileWriter.DidNotReceive().Write(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<XElement>());
    }

    [Fact]
    public void Save_FlagOnAndGuardClear_AttemptsWrite()
    {
        _options.ExportMappingsToUSyncOnSave = true;
        var handler = CreateHandler();

        handler.Handle(new SchemaMappingSavedNotification("article", Guid.NewGuid()));

        _fileWriter.Received(1).Write(Arg.Any<string>(), "article", Arg.Any<XElement>());
    }

    [Fact]
    public void Save_WriteThrows_DoesNotPropagate()
    {
        _options.ExportMappingsToUSyncOnSave = true;
        _fileWriter
            .When(w => w.Write(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<XElement>()))
            .Do(_ => throw new IOException("read-only content root"));
        var handler = CreateHandler();

        var act = () => handler.Handle(new SchemaMappingSavedNotification("article", Guid.NewGuid()));

        act.Should().NotThrow("a failed export must never break the user's save");
    }

    [Fact]
    public void Delete_FlagOff_DoesNotDelete()
    {
        _options.ExportMappingsToUSyncOnSave = false;
        var handler = CreateHandler();

        handler.Handle(new SchemaMappingDeletedNotification("article", Guid.NewGuid()));

        _fileWriter.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void Delete_FlagOn_AttemptsDelete()
    {
        _options.ExportMappingsToUSyncOnSave = true;
        var handler = CreateHandler();

        handler.Handle(new SchemaMappingDeletedNotification("article", Guid.NewGuid()));

        _fileWriter.Received(1).Delete(Arg.Any<string>(), "article");
    }
}
