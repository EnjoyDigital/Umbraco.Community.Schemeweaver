namespace Umbraco.Community.SchemeWeaver.AI.Models;

/// <summary>
/// AI-generated schema type suggestions for a single content type, used in bulk analysis.
/// </summary>
public class BulkSchemaTypeSuggestion
{
    public string ContentTypeAlias { get; set; } = string.Empty;
    public string? ContentTypeName { get; set; }
    public SchemaTypeSuggestion[] Suggestions { get; set; } = [];
}
