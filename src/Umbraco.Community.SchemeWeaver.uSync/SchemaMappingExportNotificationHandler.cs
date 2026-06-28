using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Community.SchemeWeaver;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Exports a SchemeWeaver mapping to its uSync data folder whenever it is saved
/// or deleted, so the change is ready to commit to source control. Gated behind
/// the SchemeWeaver-owned, default-off <see cref="SchemeWeaverOptions.ExportMappingsToUSyncOnSave"/>
/// flag (NOT uSync's global ExportOnSave, which would be a surprise-write
/// vector). The save path delegates to <see cref="IMappingExporter"/> so the write
/// uses one code path shared with the on-demand export endpoint; all I/O is
/// guarded so a read-only content root can never break the user's save.
/// </summary>
public class SchemaMappingExportNotificationHandler :
    INotificationHandler<SchemaMappingSavedNotification>,
    INotificationHandler<SchemaMappingDeletedNotification>
{
    private readonly IMappingExporter _exporter;
    private readonly IMappingFileWriter _fileWriter;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptions<SchemeWeaverOptions> _options;
    private readonly ILogger<SchemaMappingExportNotificationHandler> _logger;

    public SchemaMappingExportNotificationHandler(
        IMappingExporter exporter,
        IMappingFileWriter fileWriter,
        IHostEnvironment hostEnvironment,
        IOptions<SchemeWeaverOptions> options,
        ILogger<SchemaMappingExportNotificationHandler> logger)
    {
        _exporter = exporter;
        _fileWriter = fileWriter;
        _hostEnvironment = hostEnvironment;
        _options = options;
        _logger = logger;
    }

    public void Handle(SchemaMappingSavedNotification notification)
    {
        if (!ShouldExport())
            return;

        // The exporter captures per-item failures (e.g. read-only root) rather than throwing,
        // so the user's save is never broken by a failed export.
        var result = _exporter.Export(notification.ContentTypeAlias);
        var item = result.Items.FirstOrDefault();
        if (item is { Written: true })
            _logger.LogInformation("Exported SchemeWeaver mapping for {Alias} to {Folder}", notification.ContentTypeAlias, result.Folder);
        else if (item is { Written: false })
            _logger.LogWarning("Export of SchemeWeaver mapping for {Alias} did not write: {Error} — the save itself succeeded", notification.ContentTypeAlias, item.Error);
    }

    public void Handle(SchemaMappingDeletedNotification notification)
    {
        if (!ShouldExport())
            return;

        try
        {
            var folder = SchemeWeaverMappingPaths.ResolveWriteFolder(_hostEnvironment.ContentRootPath);
            _fileWriter.Delete(folder, notification.ContentTypeAlias);
            _logger.LogInformation("Removed exported SchemeWeaver mapping for {Alias} from {Folder}", notification.ContentTypeAlias, folder);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing exported SchemeWeaver mapping for {Alias} — the delete itself succeeded", notification.ContentTypeAlias);
        }
    }

    /// <summary>
    /// Export only when the owned flag is on AND we're not inside a uSync import (the import
    /// writes through the repository, so re-exporting would be pointless churn).
    /// </summary>
    private bool ShouldExport()
        => _options.Value.ExportMappingsToUSyncOnSave && !SchemeWeaverImportGuard.IsImporting;
}
