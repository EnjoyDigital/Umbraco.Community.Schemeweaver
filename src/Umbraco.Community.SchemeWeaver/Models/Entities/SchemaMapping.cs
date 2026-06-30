using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Umbraco.Community.SchemeWeaver.Models.Entities;

/// <summary>
/// Maps an Umbraco content type to a Schema.org type.
/// </summary>
[TableName(SchemeWeaverConstants.Tables.SchemaMapping)]
[PrimaryKey("Id", AutoIncrement = true)]
public class SchemaMapping
{
    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("ContentTypeAlias")]
    [Index(IndexTypes.UniqueNonClustered)]
    public string ContentTypeAlias { get; set; } = string.Empty;

    [Column("ContentTypeKey")]
    public Guid ContentTypeKey { get; set; }

    [Column("SchemaTypeName")]
    public string SchemaTypeName { get; set; } = string.Empty;

    [Column("IsEnabled")]
    public bool IsEnabled { get; set; }

    [Column("CreatedDate")]
    public DateTime CreatedDate { get; set; }

    [Column("UpdatedDate")]
    public DateTime UpdatedDate { get; set; }

    [Column("IsInherited")]
    public bool IsInherited { get; set; }

    /// <summary>
    /// Optional @id template. When set, overrides the default {url}#{type} convention.
    /// Supports tokens: {url}, {type}, {key}, {culture}, {siteUrl}.
    /// </summary>
    [Column("IdOverride")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? IdOverride { get; set; }

    /// <summary>
    /// Returns a shallow copy. Every member is a value type or string, so this is a full
    /// defensive copy — used by the cached repository so callers (e.g. SaveMapping, which
    /// fetches-then-mutates an entity) never mutate the shared cached snapshot.
    /// </summary>
    public SchemaMapping Clone() => (SchemaMapping)MemberwiseClone();
}
