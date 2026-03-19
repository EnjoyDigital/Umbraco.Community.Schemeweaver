namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// DTO for a schema mapping with its property mappings.
/// </summary>
public class SchemaMappingDto
{
    public string ContentTypeAlias { get; set; } = string.Empty;
    public Guid ContentTypeKey { get; set; }
    public string SchemaTypeName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public List<PropertyMappingDto> PropertyMappings { get; set; } = new();
}

/// <summary>
/// DTO for a single property mapping.
/// </summary>
public class PropertyMappingDto
{
    public string SchemaPropertyName { get; set; } = string.Empty;
    public string SourceType { get; set; } = "property";
    public string? ContentTypePropertyAlias { get; set; }
    public string? SourceContentTypeAlias { get; set; }
    public string? TransformType { get; set; }
    public bool IsAutoMapped { get; set; }
    public string? StaticValue { get; set; }
    public string? NestedSchemaTypeName { get; set; }
}
