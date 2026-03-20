namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// Information about a Schema.org type.
/// </summary>
public class SchemaTypeInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ParentTypeName { get; set; }
    public int PropertyCount { get; set; }
}
