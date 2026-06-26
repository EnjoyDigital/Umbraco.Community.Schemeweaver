using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Community.SchemeWeaver;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using uSync.Core.Serialization;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Exports a SchemeWeaver mapping to its uSync data folder whenever it is saved
/// or deleted, so the change is ready to commit to source control. Gated behind
/// the SchemeWeaver-owned, default-off <see cref="SchemeWeaverOptions.ExportMappingsToUSyncOnSave"/>
/// flag (NOT uSync's global ExportOnSave, which would be a surprise-write
/// vector). All file I/O is wrapped in try/catch so a read-only content root can
/// never break the user's save.
/// </summary>
public class SchemaMappingExportNotificationHandler :
    INotificationHandler<SchemaMappingSavedNotification>,
    INotificationHandler<SchemaMappingDeletedNotification>
{
    private readonly SyncSerializerCollection _serializers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IMappingFileWriter _fileWriter;
    private readonly IOptions<SchemeWeaverOptions> _options;
    private readonly ILogger<SchemaMappingExportNotificationHandler> _logger;

    public SchemaMappingExportNotificationHandler(
        SyncSerializerCollection serializers,
        IServiceScopeFactory scopeFactory,
        IHostEnvironment hostEnvironment,
        IMappingFileWriter fileWriter,
        IOptions<SchemeWeaverOptions> options,
        ILogger<SchemaMappingExportNotificationHandler> logger)
    {
        _serializers = serializers;
        _scopeFactory = scopeFactory;
        _hostEnvironment = hostEnvironment;
        _fileWriter = fileWriter;
        _options = options;
        _logger = logger;
    }

    public void Handle(SchemaMappingSavedNotification notification)
    {
        if (!ShouldExport())
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
            var item = repository.GetByContentTypeAlias(notification.ContentTypeAlias);
            if (item is null)
            {
                _logger.LogDebug("No mapping found for {Alias} to export — skipping", notification.ContentTypeAlias);
                return;
            }

            var serializer = _serializers.OfType<SchemaMappingSerializer>().FirstOrDefault();
            if (serializer is null)
            {
                _logger.LogWarning("SchemaMappingSerializer not found — cannot export mapping for {Alias}", notification.ContentTypeAlias);
                return;
            }

            var attempt = serializer
                .SerializeAsync(item, new SyncSerializerOptions())
                .GetAwaiter()
                .GetResult();

            if (!attempt.Success || attempt.Item is null)
            {
                _logger.LogWarning("Failed to serialise mapping for {Alias} — skipping export", notification.ContentTypeAlias);
                return;
            }

            var folder = SchemeWeaverMappingPaths.ResolveWriteFolder(_hostEnvironment.ContentRootPath);
            _fileWriter.Write(folder, notification.ContentTypeAlias, attempt.Item);

            _logger.LogInformation("Exported SchemeWeaver mapping for {Alias} to {Folder}", notification.ContentTypeAlias, folder);
        }
        catch (Exception ex)
        {
            // A failed export must never break the user's save (e.g. read-only
            // content root on a container or Azure App Service).
            _logger.LogWarning(ex, "Error exporting SchemeWeaver mapping for {Alias} — the save itself succeeded", notification.ContentTypeAlias);
        }
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
    /// Export only when the owned flag is on AND we're not inside the first-boot
    /// import (the import writes through the repository, so re-exporting would be
    /// pointless churn).
    /// </summary>
    private bool ShouldExport()
        => _options.Value.ExportMappingsToUSyncOnSave && !SchemeWeaverImportGuard.IsImporting;
}
