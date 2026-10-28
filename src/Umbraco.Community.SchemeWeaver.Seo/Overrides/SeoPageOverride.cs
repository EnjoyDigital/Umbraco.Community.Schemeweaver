using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Umbraco.Community.SchemeWeaver.Seo.Overrides;

/// <summary>
/// A per-page override of the content type's mapping: exclude the page from structured data,
/// and/or emit a subtype of the mapped Schema.org type. Invariant (one row per content node).
/// </summary>
[TableName(SchemeWeaverSeoConstants.Tables.PageOverride)]
[PrimaryKey("Id", AutoIncrement = true)]
public class SeoPageOverride
{
    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("ContentKey")]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_SchemeWeaverSeoPageOverride_ContentKey")]
    public Guid ContentKey { get; set; }

    [Column("SchemaTypeName")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? SchemaTypeName { get; set; }

    [Column("IsExcluded")]
    public bool IsExcluded { get; set; }

    [Column("UpdatedDate")]
    public DateTime UpdatedDate { get; set; }

    [Column("UpdatedByUserKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? UpdatedByUserKey { get; set; }

    public SeoPageOverride Clone() => (SeoPageOverride)MemberwiseClone();
}
