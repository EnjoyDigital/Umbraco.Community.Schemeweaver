namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Resolves the on-disk ScheweWeaver mappings folder under the uSync data root.
/// The contract is defined by <see cref="SchemaMappingImportNotificationHandler"/>,
/// which reads from <c>{ContentRoot}/uSync/{v18|v17}/SchemeWeaverMappings</c>, so
/// export must write to the same place to round-trip. The version sub-folder is
/// chosen at runtime (rather than hardcoded) by probing which uSync version
/// folder the host actually uses, defaulting to the current convention.
/// </summary>
internal static class SchemeWeaverMappingPaths
{
    public const string RootFolderName = "uSync";
    public const string MappingsFolderName = "SchemeWeaverMappings";

    /// <summary>
    /// uSync data-folder version sub-folders, newest first. v18 is the current
    /// convention; v17 covers installs upgraded in place.
    /// </summary>
    private static readonly string[] Versions = ["v18", "v17"];

    /// <summary>
    /// The folder to write a mapping file to. Prefers the version sub-folder the
    /// host's uSync install already uses (so SchemeWeaver mappings sit alongside
    /// the doc types they belong to); falls back to the current convention.
    /// </summary>
    public static string ResolveWriteFolder(string contentRootPath)
    {
        var uSyncRoot = Path.Combine(contentRootPath, RootFolderName);
        var version = Versions.FirstOrDefault(v => Directory.Exists(Path.Combine(uSyncRoot, v)))
                      ?? Versions[0];
        return Path.Combine(uSyncRoot, version, MappingsFolderName);
    }
}
