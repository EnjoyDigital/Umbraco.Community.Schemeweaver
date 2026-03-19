namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// Response containing a JSON-LD preview with validation results.
/// </summary>
public class JsonLdPreviewResponse
{
    public string JsonLd { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}
