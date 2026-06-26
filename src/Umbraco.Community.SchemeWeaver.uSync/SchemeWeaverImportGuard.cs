namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// Belt-and-braces re-entrancy guard for the uSync first-boot import. While an
/// import is in progress on the current async flow, the export-on-save handler
/// must not write anything back to disk. Service-layer notification publishing
/// already makes the loop structurally impossible (the importer writes through
/// the repository, not the service), but this guard protects against any future
/// path that imports through the service.
/// </summary>
internal static class SchemeWeaverImportGuard
{
    private static readonly AsyncLocal<bool> Importing = new();

    /// <summary>True when an import is running on the current async flow.</summary>
    public static bool IsImporting => Importing.Value;

    /// <summary>
    /// Marks the current async flow as importing until the returned token is
    /// disposed. Use with <c>using</c> around the import loop.
    /// </summary>
    public static IDisposable Enter()
    {
        Importing.Value = true;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Importing.Value = false;
    }
}
