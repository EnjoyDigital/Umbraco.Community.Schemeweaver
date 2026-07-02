using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Tiny seam over the file system so the export-on-save handler's write/delete
/// can be unit-tested without touching disk, and so a read-only content root
/// (container / Azure App Service) surfaces as a catchable exception rather than
/// a process crash.
/// </summary>
public interface IMappingFileWriter
{
    /// <summary>
    /// Writes <paramref name="xml"/> to <c>{folder}/{alias}.config</c>, creating the folder if
    /// needed. Returns false when the alias is refused as unsafe (not a plain file name).
    /// </summary>
    bool Write(string folder, string alias, XElement xml);

    /// <summary>
    /// Removes <c>{folder}/{alias}.config</c> if it exists. Returns false when the alias is
    /// refused as unsafe (not a plain file name).
    /// </summary>
    bool Delete(string folder, string alias);
}

/// <summary>
/// Default <see cref="IMappingFileWriter"/> that writes to the local file system.
/// Aliases that are not plain file names (path separators, traversal segments, invalid
/// characters) are REJECTED rather than sanitised — a transforming sanitiser would break the
/// alias↔filename bijection that filename-keyed drift detection relies on.
/// </summary>
public class MappingFileWriter : IMappingFileWriter
{
    private readonly ILogger<MappingFileWriter>? _logger;

    public MappingFileWriter(ILogger<MappingFileWriter>? logger = null)
    {
        _logger = logger;
    }

    public bool Write(string folder, string alias, XElement xml)
    {
        if (!SchemeWeaverMappingPaths.IsSafeAlias(alias))
        {
            _logger?.LogWarning("Refusing to write uSync mapping file for unsafe alias {Alias}", alias);
            return false;
        }

        Directory.CreateDirectory(folder);
        xml.Save(Path.Join(folder, $"{alias}.config"));
        return true;
    }

    public bool Delete(string folder, string alias)
    {
        if (!SchemeWeaverMappingPaths.IsSafeAlias(alias))
        {
            _logger?.LogWarning("Refusing to delete uSync mapping file for unsafe alias {Alias}", alias);
            return false;
        }

        var path = Path.Join(folder, $"{alias}.config");
        if (File.Exists(path))
            File.Delete(path);
        return true;
    }
}
