using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using uSync.Core.Models;
using uSync.Core.Serialization;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Imports SchemeWeaver mapping XML files on first boot, running after uSync's standard
/// import has created doc types, content, media, etc. Reads XML files from the
/// <c>uSync/v17/SchemeWeaverMappings/</c> folder and uses the existing
/// <see cref="SchemaMappingSerializer"/> to deserialise them into the database.
/// </summary>
public class SchemaMappingImportNotificationHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly SyncSerializerCollection _serializers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<SchemaMappingImportNotificationHandler> _logger;

    public SchemaMappingImportNotificationHandler(
        SyncSerializerCollection serializers,
        IServiceScopeFactory scopeFactory,
        IHostEnvironment hostEnvironment,
        ILogger<SchemaMappingImportNotificationHandler> logger)
    {
        _serializers = serializers;
        _scopeFactory = scopeFactory;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        var mappingsFolder = Path.Combine(_hostEnvironment.ContentRootPath, "uSync", "v17", "SchemeWeaverMappings");
        if (!Directory.Exists(mappingsFolder))
        {
            _logger.LogDebug("No SchemeWeaverMappings folder found at {Path} — skipping import", mappingsFolder);
            return;
        }

        // Check if mappings already exist (scoped repository access)
        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
            var existing = repository.GetAll();
            if (existing.Any())
            {
                _logger.LogDebug("SchemeWeaver mappings already exist — skipping uSync import");
                return;
            }
        }

        var xmlFiles = Directory.GetFiles(mappingsFolder, "*.config", SearchOption.AllDirectories);
        if (xmlFiles.Length == 0)
        {
            _logger.LogDebug("No mapping files found in {Path}", mappingsFolder);
            return;
        }

        _logger.LogInformation("Importing {Count} SchemeWeaver mappings from uSync", xmlFiles.Length);

        // Find the SchemaMapping serializer from the uSync collection
        var serializer = _serializers.OfType<SchemaMappingSerializer>().FirstOrDefault();
        if (serializer is null)
        {
            _logger.LogWarning("SchemaMappingSerializer not found in uSync serializer collection — cannot import mappings");
            return;
        }

        var imported = 0;
        foreach (var file in xmlFiles)
        {
            try
            {
                var xml = XElement.Load(file);
                var result = await serializer.DeserializeAsync(xml, new SyncSerializerOptions());
                if (result.Success)
                    imported++;
                else
                    _logger.LogWarning("Failed to import mapping from {File}: {Message}", file, result.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error importing mapping from {File}", file);
            }
        }

        _logger.LogInformation("Imported {Imported}/{Total} SchemeWeaver mappings", imported, xmlFiles.Length);
    }
}
