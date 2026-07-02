using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using uSync.Core.Models;
using uSync.Core.Serialization;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Imports SchemeWeaver mapping XML files on boot, running after uSync's standard import has
/// created doc types, content, media, etc. Reads <c>.config</c> files from the
/// <c>uSync/{v18|v17}/SchemeWeaverMappings/</c> folder and uses the existing
/// <see cref="SchemaMappingSerializer"/> to deserialise them into the database. Behaviour is
/// governed by <see cref="SchemeWeaverOptions.USyncBootImport"/>:
/// <see cref="BootImportMode.Off"/> (default) = first-boot-only seeding,
/// <see cref="BootImportMode.Seed"/> = create-missing on every boot,
/// <see cref="BootImportMode.Upsert"/> = disk-wins on every boot.
/// </summary>
public class SchemaMappingImportNotificationHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly SyncSerializerCollection _serializers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptions<SchemeWeaverOptions> _options;
    private readonly ILogger<SchemaMappingImportNotificationHandler> _logger;

    public SchemaMappingImportNotificationHandler(
        SyncSerializerCollection serializers,
        IServiceScopeFactory scopeFactory,
        IHostEnvironment hostEnvironment,
        IOptions<SchemeWeaverOptions> options,
        ILogger<SchemaMappingImportNotificationHandler> logger)
    {
        _serializers = serializers;
        _scopeFactory = scopeFactory;
        _hostEnvironment = hostEnvironment;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        // uSync's data-folder convention moves to "v18" on Umbraco 18, but existing
        // installs may still hold their mappings under the older "v17" folder. Probe
        // the current convention first and fall back to the previous one. Require the
        // folder to actually contain mapping files so an empty "v18" (e.g. created by
        // an in-place upgrade) cannot shadow a populated legacy "v17".
        var mappingsFolder = new[] { "v18", "v17" }
            .Select(version => Path.Join(_hostEnvironment.ContentRootPath, "uSync", version, "SchemeWeaverMappings"))
            .FirstOrDefault(path => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.config", SearchOption.AllDirectories).Any());

        if (mappingsFolder is null)
        {
            var defaultPath = Path.Join(_hostEnvironment.ContentRootPath, "uSync", "v18", "SchemeWeaverMappings");
            _logger.LogDebug("No SchemeWeaverMappings folder found (looked under v18/v17, e.g. {Path}) — skipping import", defaultPath);
            return;
        }

        var mode = _options.Value.USyncBootImport;

        // Snapshot existing mapping aliases once (scoped repository access).
        HashSet<string> existingAliases;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
            existingAliases = repository.GetAll()
                .Select(m => m.ContentTypeAlias)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Off (default) = first-boot-only seeding: once any mapping exists, do nothing on boot
        // so backoffice edits are never overwritten on restart.
        if (mode == BootImportMode.Off && existingAliases.Count > 0)
        {
            _logger.LogDebug("USyncBootImport=Off and mappings already exist — skipping boot import (first-boot-only)");
            return;
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
        // Belt-and-braces: suppress export-on-save while importing so a future
        // service-routed import path can't trigger an import → export loop.
        using (SchemeWeaverImportGuard.Enter())
        {
            foreach (var file in xmlFiles)
            {
                try
                {
                    var xml = XElement.Load(file);

                    // Seed mode never overwrites an existing mapping (create-missing only).
                    if (mode == BootImportMode.Seed)
                    {
                        var alias = xml.Element("Info")?.Element("ContentTypeAlias")?.Value
                                    ?? Path.GetFileNameWithoutExtension(file);
                        if (existingAliases.Contains(alias))
                        {
                            _logger.LogDebug("Seed mode: mapping {Alias} already exists — leaving the DB value untouched", alias);
                            continue;
                        }
                    }

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
        }

        _logger.LogInformation("Imported {Imported}/{Total} SchemeWeaver mappings", imported, xmlFiles.Length);
    }
}
