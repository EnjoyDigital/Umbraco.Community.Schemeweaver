using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// Schema.org property enriched with a server-calculated ranking score so the
/// nested-type modal can surface the most relevant properties first.
/// </summary>
public class RankedSchemaPropertyInfo : SchemaPropertyInfo
{
    /// <summary>
    /// Integer score 0-100 expressing how likely this property is to be useful
    /// when mapping a nested type for the containing Schema.org type.
    /// </summary>
    public int Confidence { get; set; }

    /// <summary>
    /// True when the property is considered "popular" for the containing
    /// Schema.org type (confidence >= 60). UI surfaces these first.
    /// </summary>
    public bool IsPopular { get; set; }
}
