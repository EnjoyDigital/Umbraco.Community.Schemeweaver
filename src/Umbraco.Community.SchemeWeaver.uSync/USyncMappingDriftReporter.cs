using System.Xml.Linq;
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
/// Real <see cref="IMappingDriftReporter"/>: compares each DB mapping against its on-disk uSync
/// <c>.config</c> by re-serialising the DB entity with <see cref="SchemaMappingSerializer"/> and
/// diffing the XML (<see cref="XNode.DeepEquals"/>). The serializer output is deterministic and
/// timestamp-free, so equal XML means in-sync. Read-only — never writes.
/// </summary>
public sealed class USyncMappingDriftReporter : IMappingDriftReporter
{
    private readonly SyncSerializerCollection _serializers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<USyncMappingDriftReporter> _logger;

    public USyncMappingDriftReporter(
        SyncSerializerCollection serializers,
        IServiceScopeFactory scopeFactory,
        IHostEnvironment hostEnvironment,
        ILogger<USyncMappingDriftReporter> logger)
    {
        _serializers = serializers;
        _scopeFactory = scopeFactory;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public bool IsAvailable => true;

    public MappingDriftReportDto GetReport()
    {
        var folder = SchemeWeaverMappingPaths.ResolveWriteFolder(_hostEnvironment.ContentRootPath);
        var serializer = _serializers.OfType<SchemaMappingSerializer>().FirstOrDefault();

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();

        var entries = new List<MappingDriftEntryDto>();
        var dbAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in repository.GetAll())
        {
            dbAliases.Add(mapping.ContentTypeAlias);
            entries.Add(new MappingDriftEntryDto
            {
                ContentTypeAlias = mapping.ContentTypeAlias,
                Status = Compare(mapping, folder, serializer)
            });
        }

        // Orphaned on-disk configs: a .config with no matching DB row.
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.config", SearchOption.AllDirectories))
            {
                var alias = Path.GetFileNameWithoutExtension(file);
                if (!dbAliases.Contains(alias))
                    entries.Add(new MappingDriftEntryDto { ContentTypeAlias = alias, Status = MappingDriftStatus.DiskOnly });
            }
        }

        return new MappingDriftReportDto { UsyncAvailable = true, Items = entries };
    }

    public string GetStatus(string contentTypeAlias)
    {
        var folder = SchemeWeaverMappingPaths.ResolveWriteFolder(_hostEnvironment.ContentRootPath);
        var serializer = _serializers.OfType<SchemaMappingSerializer>().FirstOrDefault();

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();

        var mapping = repository.GetByContentTypeAlias(contentTypeAlias);
        if (mapping is null)
        {
            var path = Path.Combine(folder, $"{contentTypeAlias}.config");
            return File.Exists(path) ? MappingDriftStatus.DiskOnly : MappingDriftStatus.DbOnly;
        }

        return Compare(mapping, folder, serializer);
    }

    /// <summary>
    /// Compares a single DB mapping against its on-disk config: db-only when no file exists,
    /// in-sync when the re-serialised XML matches, content-differs otherwise (or on any error).
    /// </summary>
    private string Compare(SchemaMapping mapping, string folder, SchemaMappingSerializer? serializer)
    {
        var path = Path.Combine(folder, $"{mapping.ContentTypeAlias}.config");
        if (!File.Exists(path))
            return MappingDriftStatus.DbOnly;

        if (serializer is null)
            return MappingDriftStatus.ContentDiffers;

        try
        {
            var attempt = serializer.SerializeAsync(mapping, new SyncSerializerOptions()).GetAwaiter().GetResult();
            if (!attempt.Success || attempt.Item is null)
                return MappingDriftStatus.ContentDiffers;

            var diskXml = XElement.Load(path);
            return ContentEquals(attempt.Item, diskXml)
                ? MappingDriftStatus.InSync
                : MappingDriftStatus.ContentDiffers;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compare mapping {Alias} against its uSync config", mapping.ContentTypeAlias);
            return MappingDriftStatus.ContentDiffers;
        }
    }

    /// <summary>
    /// Structural equality of two mapping configs that ignores cosmetic, semantically-irrelevant
    /// differences so drift stays a trustworthy signal rather than noise:
    /// <list type="bullet">
    ///   <item>the uSync ROOT bookkeeping attributes (Key/Alias/Level/Flags) — they duplicate the
    ///   identity already in &lt;Info&gt; and vary by uSync version/generator path;</item>
    ///   <item>GUID value casing (e.g. ContentTypeKey) — GUIDs are case-insensitive identifiers,
    ///   and older fixtures stored them upper-case while the current serializer emits lower-case.</item>
    /// </list>
    /// </summary>
    private static bool ContentEquals(XElement a, XElement b)
        => XNode.DeepEquals(Normalize(a), Normalize(b));

    private static XElement Normalize(XElement element)
    {
        var clone = new XElement(element);
        clone.RemoveAttributes();

        foreach (var leaf in clone.DescendantsAndSelf())
        {
            // Canonicalise GUID leaf values to lower-case so upper/lower-case GUIDs compare equal.
            if (!leaf.HasElements && Guid.TryParse(leaf.Value, out var guid))
                leaf.Value = guid.ToString();
        }

        return clone;
    }
}
