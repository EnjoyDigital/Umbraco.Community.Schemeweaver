namespace Umbraco.Community.SchemeWeaver.AI.Models;

/// <summary>
/// AI-generated suggestion for which Schema.org type best fits a content type.
/// </summary>
public class SchemaTypeSuggestion
{
    public string SchemaTypeName { get; set; } = string.Empty;
    public int Confidence { get; set; }
    public string? Reasoning { get; set; }
}
