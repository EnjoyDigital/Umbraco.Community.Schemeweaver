using System.Xml.Linq;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Tiny seam over the file system so the export-on-save handler's write/delete
/// can be unit-tested without touching disk, and so a read-only content root
/// (container / Azure App Service) surfaces as a catchable exception rather than
/// a process crash.
/// </summary>
public interface IMappingFileWriter
{
    /// <summary>Writes <paramref name="xml"/> to <c>{folder}/{alias}.config</c>, creating the folder if needed.</summary>
    void Write(string folder, string alias, XElement xml);

    /// <summary>Removes <c>{folder}/{alias}.config</c> if it exists.</summary>
    void Delete(string folder, string alias);
}

/// <summary>Default <see cref="IMappingFileWriter"/> that writes to the local file system.</summary>
public class MappingFileWriter : IMappingFileWriter
{
    public void Write(string folder, string alias, XElement xml)
    {
        Directory.CreateDirectory(folder);
        xml.Save(Path.Combine(folder, $"{alias}.config"));
    }

    public void Delete(string folder, string alias)
    {
        var path = Path.Combine(folder, $"{alias}.config");
        if (File.Exists(path))
            File.Delete(path);
    }
}
