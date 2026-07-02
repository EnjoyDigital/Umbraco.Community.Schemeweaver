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

    // Read-only diagnostics that hit the DB + disk together are inherently exposed to transient
    // infrastructure hiccups, and this one must never surface them as a 500:
    //   * under shared-cache SQLite a read that races a concurrent write can raise SQLITE_LOCKED,
    //     which the provider does NOT auto-retry (unlike SQLITE_BUSY);
    //   * the uSync folder can be written (export/import) while we enumerate it.
    // We therefore retry a couple of times before letting a genuinely persistent failure propagate.
    private const int MaxAttempts = 3;

    public MappingDriftReportDto GetReport()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return BuildReport();
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Mapping drift report attempt {Attempt}/{Max} failed; retrying", attempt, MaxAttempts);
                Thread.Sleep(25 * attempt);
            }
        }
    }

    private MappingDriftReportDto BuildReport()
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
        foreach (var alias in EnumerateOrphanConfigAliases(folder, dbAliases))
            entries.Add(new MappingDriftEntryDto { ContentTypeAlias = alias, Status = MappingDriftStatus.DiskOnly });

        return new MappingDriftReportDto { UsyncAvailable = true, Items = entries };
    }

    // Directory.EnumerateFiles streams lazily, so a file or sub-folder vanishing mid-walk (uSync
    // writing the folder concurrently) would throw partway through iteration and abort the whole
    // report. Materialise the list inside a guard so a transient I/O race degrades to "no orphans
    // found this pass" rather than failing the diagnostic.
    private IEnumerable<string> EnumerateOrphanConfigAliases(string folder, HashSet<string> dbAliases)
    {
        if (!Directory.Exists(folder))
            return [];

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.config", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Partial uSync folder enumeration whilst computing mapping drift");
            return [];
        }

        return files
            .Select(Path.GetFileNameWithoutExtension)
            .Where(alias => !string.IsNullOrEmpty(alias) && !dbAliases.Contains(alias!))
            .Select(alias => alias!);
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
            var path = Path.Join(folder, $"{contentTypeAlias}.config");
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
        var path = Path.Join(folder, $"{mapping.ContentTypeAlias}.config");
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
