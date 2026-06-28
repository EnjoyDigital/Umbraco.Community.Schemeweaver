using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Community.SchemeWeaver;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.uSync;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Unit-tests the GATING of the uSync export-on-save handler. The save path now delegates the
/// write to <see cref="IMappingExporter"/> (the full serialise → disk round-trip lives in
/// <see cref="USyncMappingExporterTests"/>); the delete path still uses <see cref="IMappingFileWriter"/>.
/// </summary>
public class SchemaMappingExportNotificationHandlerTests
{
    private readonly IMappingExporter _exporter = Substitute.For<IMappingExporter>();
    private readonly IMappingFileWriter _fileWriter = Substitute.For<IMappingFileWriter>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly SchemeWeaverOptions _options = new();

    public SchemaMappingExportNotificationHandlerTests()
    {
        _hostEnvironment.ContentRootPath.Returns(Path.GetTempPath());

        // Default: a successful export of the one requested mapping.
        _exporter.Export(Arg.Any<string?>()).Returns(ci => new MappingExportResultDto
        {
            UsyncAvailable = true,
            Folder = Path.GetTempPath(),
            Items = [new MappingExportItemDto { Alias = ci.Arg<string?>() ?? "all", Written = true }]
        });
    }

    private SchemaMappingExportNotificationHandler CreateHandler()
        => new(
            _exporter,
            _fileWriter,
            _hostEnvironment,
            Options.Create(_options),
            Substitute.For<ILogger<SchemaMappingExportNotificationHandler>>());

    [Fact]
    public void Save_FlagOff_DoesNotExport()
    {
        _options.ExportMappingsToUSyncOnSave = false;
        var handler = CreateHandler();

        handler.Handle(new SchemaMappingSavedNotification("article", Guid.NewGuid()));

        _exporter.DidNotReceive().Export(Arg.Any<string?>());
    }

    [Fact]
    public void Save_ImportGuardSet_DoesNotExport()
    {
        _options.ExportMappingsToUSyncOnSave = true;
        var handler = CreateHandler();

        using (SchemeWeaverImportGuard.Enter())
        {
            handler.Handle(new SchemaMappingSavedNotification("article", Guid.NewGuid()));
        }

        _exporter.DidNotReceive().Export(Arg.Any<string?>());
    }

    [Fact]
    public void Save_FlagOnAndGuardClear_DelegatesToExporter()
    {
        _options.ExportMappingsToUSyncOnSave = true;
        var handler = CreateHandler();

        handler.Handle(new SchemaMappingSavedNotification("article", Guid.NewGuid()));

        _exporter.Received(1).Export("article");
    }

    [Fact]
    public void Save_ExportReportsFailure_DoesNotThrow()
    {
        _options.ExportMappingsToUSyncOnSave = true;
        _exporter.Export(Arg.Any<string?>()).Returns(new MappingExportResultDto
        {
            UsyncAvailable = true,
            Folder = Path.GetTempPath(),
            Items = [new MappingExportItemDto { Alias = "article", Written = false, Error = "read-only content root" }]
        });
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
