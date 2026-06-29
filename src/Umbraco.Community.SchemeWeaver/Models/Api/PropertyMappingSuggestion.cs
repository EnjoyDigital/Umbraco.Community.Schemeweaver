namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// A suggested mapping between a Schema.org property and an Umbraco content type property.
/// </summary>
public class PropertyMappingSuggestion
{
    public string SchemaPropertyName { get; set; } = string.Empty;
    public string? SchemaPropertyType { get; set; }
    public string? SuggestedContentTypePropertyAlias { get; set; }
    public string SuggestedSourceType { get; set; } = "property";
    public int Confidence { get; set; }
    public bool IsAutoMapped { get; set; }
    public string? EditorAlias { get; set; }
    public List<string> AcceptedTypes { get; set; } = [];
    public bool IsComplexType { get; set; }
    public string? SuggestedNestedSchemaTypeName { get; set; }
    public string? SuggestedResolverConfig { get; set; }

    /// <summary>
    /// For the <c>static</c> source type: the fixed literal value to emit. Null for every
    /// other source type. Carried here so an AI suggestion of a static mapping survives
    /// round-tripping through the suggestion list.
    /// </summary>
    public string? StaticValue { get; set; }

    /// <summary>
    /// For <c>reference</c> source type: the graph piece key the suggestion
    /// points at (e.g. <c>"organization"</c>). Null for every other source type.
    /// </summary>
    public string? SuggestedTargetPieceKey { get; set; }
}
