using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using uSync.Core.Serialization;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Real <see cref="IMappingExporter"/>: serialises mappings with
/// <see cref="SchemaMappingSerializer"/> and writes them via <see cref="IMappingFileWriter"/>.
/// Per-item failures (e.g. a read-only content root) are captured in the result rather than
/// thrown, so a partial export still reports what succeeded.
/// </summary>
public sealed class USyncMappingExporter : IMappingExporter
{
    private readonly SyncSerializerCollection _serializers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IMappingFileWriter _fileWriter;
    private readonly ILogger<USyncMappingExporter> _logger;

    public USyncMappingExporter(
        SyncSerializerCollection serializers,
        IServiceScopeFactory scopeFactory,
        IHostEnvironment hostEnvironment,
        IMappingFileWriter fileWriter,
        ILogger<USyncMappingExporter> logger)
    {
        _serializers = serializers;
        _scopeFactory = scopeFactory;
        _hostEnvironment = hostEnvironment;
        _fileWriter = fileWriter;
        _logger = logger;
    }

    public bool IsAvailable => true;

    public MappingExportResultDto Export(string? contentTypeAlias = null)
    {
        var folder = SchemeWeaverMappingPaths.ResolveWriteFolder(_hostEnvironment.ContentRootPath);
        var items = new List<MappingExportItemDto>();
        var serializer = _serializers.OfType<SchemaMappingSerializer>().FirstOrDefault();

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();

        var mappings = string.IsNullOrWhiteSpace(contentTypeAlias)
            ? repository.GetAll().ToList()
            : repository.GetByContentTypeAlias(contentTypeAlias) is { } single ? [single] : new List<SchemaMapping>();

        foreach (var mapping in mappings)
        {
            try
            {
                if (serializer is null)
                {
                    items.Add(new MappingExportItemDto { Alias = mapping.ContentTypeAlias, Written = false, Error = "Serializer unavailable" });
                    continue;
                }

                var attempt = serializer.SerializeAsync(mapping, new SyncSerializerOptions()).GetAwaiter().GetResult();
                if (!attempt.Success || attempt.Item is null)
                {
                    items.Add(new MappingExportItemDto { Alias = mapping.ContentTypeAlias, Written = false, Error = "Serialisation failed" });
                    continue;
                }

                _fileWriter.Write(folder, mapping.ContentTypeAlias, attempt.Item);
                items.Add(new MappingExportItemDto { Alias = mapping.ContentTypeAlias, Written = true });
            }
            catch (Exception ex)
            {
                // A read-only content root surfaces here as written=false rather than throwing.
                _logger.LogWarning(ex, "Failed to export SchemeWeaver mapping for {Alias}", mapping.ContentTypeAlias);
                items.Add(new MappingExportItemDto { Alias = mapping.ContentTypeAlias, Written = false, Error = ex.Message });
            }
        }

        return new MappingExportResultDto { UsyncAvailable = true, Folder = folder, Items = items };
    }
}
