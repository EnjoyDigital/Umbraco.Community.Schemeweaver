using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Umbraco.Community.SchemeWeaver.Models.Entities;

/// <summary>
/// Maps a Schema.org property to an Umbraco content type property or other source.
/// </summary>
[TableName(SchemeWeaverConstants.Tables.PropertyMapping)]
[PrimaryKey("Id", AutoIncrement = true)]
public class PropertyMapping
{
    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("SchemaMappingId")]
    [ForeignKey(typeof(SchemaMapping), Name = "FK_PropertyMapping_SchemaMapping")]
    public int SchemaMappingId { get; set; }

    [Column("SchemaPropertyName")]
    public string SchemaPropertyName { get; set; } = string.Empty;

    [Column("SourceType")]
    public string SourceType { get; set; } = "property";

    [Column("ContentTypePropertyAlias")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? ContentTypePropertyAlias { get; set; }

    [Column("SourceContentTypeAlias")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? SourceContentTypeAlias { get; set; }

    [Column("TransformType")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? TransformType { get; set; }

    [Column("IsAutoMapped")]
    public bool IsAutoMapped { get; set; }

    [Column("StaticValue")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? StaticValue { get; set; }

    [Column("NestedSchemaTypeName")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? NestedSchemaTypeName { get; set; }

    [Column("ResolverConfig")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string? ResolverConfig { get; set; }

    [Column("DynamicRootConfig")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string? DynamicRootConfig { get; set; }
}
