namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// Full disk/DB drift report for schema mappings. <see cref="UsyncAvailable"/> is false when
/// the uSync addon is not installed, in which case <see cref="Items"/> is empty.
/// </summary>
public class MappingDriftReportDto
{
    public bool UsyncAvailable { get; set; }
    public List<MappingDriftEntryDto> Items { get; set; } = [];
}

/// <summary>Drift status for one mapping. <see cref="Status"/> is a <c>MappingDriftStatus</c> code.</summary>
public class MappingDriftEntryDto
{
    public string ContentTypeAlias { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>Result of an on-demand export-to-uSync operation.</summary>
public class MappingExportResultDto
{
    public bool UsyncAvailable { get; set; }

    /// <summary>The folder mappings were written to (null when the addon is unavailable).</summary>
    public string? Folder { get; set; }

    public List<MappingExportItemDto> Items { get; set; } = [];
}

/// <summary>Per-mapping export outcome. <see cref="Written"/> is false with an <see cref="Error"/> on failure.</summary>
public class MappingExportItemDto
{
    public string Alias { get; set; } = string.Empty;
    public bool Written { get; set; }
    public string? Error { get; set; }
}

/// <summary>Request body for the export endpoint. Omit <see cref="ContentTypeAlias"/> to export all.</summary>
public class MappingExportRequest
{
    public string? ContentTypeAlias { get; set; }
}

/// <summary>
/// Lightweight target-context info so callers (e.g. the MCP) can distinguish a populated site
/// from an empty sandbox/TestHost before trusting a render.
/// </summary>
public class ServerContextDto
{
    /// <summary>True when the target has at least one published content node at the tree root.</summary>
    public bool HasPublishedContent { get; set; }

    /// <summary>True when the host appears to be the SchemeWeaver TestHost sandbox.</summary>
    public bool IsTestHost { get; set; }
}
