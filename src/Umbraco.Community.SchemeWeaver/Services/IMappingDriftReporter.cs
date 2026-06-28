using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Reports drift between schema mappings stored in the database and their on-disk uSync
/// <c>.config</c> representation. The real implementation lives in the optional
/// <c>Umbraco.Community.SchemeWeaver.uSync</c> addon (it needs the file system + serializer);
/// when that package is absent the registered <see cref="NullMappingDriftReporter"/> reports
/// <see cref="MappingDriftStatus.USyncUnavailable"/> so the management API + MCP surface stays
/// stable either way. The MCP never touches the server file system — drift is always computed
/// server-side and returned over the API.
/// </summary>
public interface IMappingDriftReporter
{
    /// <summary>True when a real uSync-backed reporter is available (the addon is installed).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Builds a full drift report covering every DB mapping plus any orphaned on-disk
    /// <c>.config</c> file that has no matching DB row (<see cref="MappingDriftStatus.DiskOnly"/>).
    /// </summary>
    MappingDriftReportDto GetReport();

    /// <summary>The drift status for a single mapping alias (see <see cref="MappingDriftStatus"/>).</summary>
    string GetStatus(string contentTypeAlias);
}

/// <summary>
/// Drift status codes, kebab-cased to match the existing <c>reachability</c> convention.
/// </summary>
public static class MappingDriftStatus
{
    /// <summary>DB mapping and on-disk config match.</summary>
    public const string InSync = "in-sync";

    /// <summary>Mapping exists in the DB but has no on-disk config (never exported).</summary>
    public const string DbOnly = "db-only";

    /// <summary>An on-disk config exists with no matching DB mapping.</summary>
    public const string DiskOnly = "disk-only";

    /// <summary>DB mapping and on-disk config both exist but differ.</summary>
    public const string ContentDiffers = "content-differs";

    /// <summary>The uSync addon is not installed, so drift cannot be computed.</summary>
    public const string USyncUnavailable = "usync-unavailable";
}

/// <summary>
/// No-op reporter used when the uSync addon is absent. Always reports the addon as
/// unavailable so callers can render an honest "drift unknown" state rather than a false
/// "in sync".
/// </summary>
public sealed class NullMappingDriftReporter : IMappingDriftReporter
{
    public bool IsAvailable => false;

    public MappingDriftReportDto GetReport() => new() { UsyncAvailable = false, Items = [] };

    public string GetStatus(string contentTypeAlias) => MappingDriftStatus.USyncUnavailable;
}
