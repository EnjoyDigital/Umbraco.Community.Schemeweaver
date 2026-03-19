namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// Request to generate an Umbraco content type from a Schema.org type.
/// </summary>
public class ContentTypeGenerationRequest
{
    public string SchemaTypeName { get; set; } = string.Empty;
    public string DocumentTypeName { get; set; } = string.Empty;
    public string DocumentTypeAlias { get; set; } = string.Empty;
    public List<string> SelectedProperties { get; set; } = [];
    public string PropertyGroupName { get; set; } = "Content";
}
