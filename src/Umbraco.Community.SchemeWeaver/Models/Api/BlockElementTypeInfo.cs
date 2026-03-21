namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// Information about a block element type available within a BlockList/BlockGrid property.
/// </summary>
public class BlockElementTypeInfo
{
    public string Alias { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Properties { get; set; } = [];
}
