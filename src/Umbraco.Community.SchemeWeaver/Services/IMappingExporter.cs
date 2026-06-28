using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// On-demand exporter that writes schema mappings to their uSync <c>.config</c> files,
/// independent of the <see cref="SchemeWeaverOptions.ExportMappingsToUSyncOnSave"/> flag —
/// this is the explicit "write config-as-code now" primitive. The real implementation lives
/// in the optional uSync addon; when absent the registered <see cref="NullMappingExporter"/>
/// reports the addon as unavailable rather than failing.
/// </summary>
public interface IMappingExporter
{
    /// <summary>True when a real uSync-backed exporter is available (the addon is installed).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Exports every mapping (when <paramref name="contentTypeAlias"/> is null/empty) or a
    /// single mapping to disk. Per-item failures (e.g. a read-only content root) are reported
    /// in the result rather than thrown.
    /// </summary>
    MappingExportResultDto Export(string? contentTypeAlias = null);
}

/// <summary>
/// No-op exporter used when the uSync addon is absent. Reports the addon as unavailable and
/// writes nothing.
/// </summary>
public sealed class NullMappingExporter : IMappingExporter
{
    public bool IsAvailable => false;

    public MappingExportResultDto Export(string? contentTypeAlias = null)
        => new() { UsyncAvailable = false, Folder = null, Items = [] };
}
