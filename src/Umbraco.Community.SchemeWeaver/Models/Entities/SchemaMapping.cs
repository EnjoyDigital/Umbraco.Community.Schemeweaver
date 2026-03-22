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
}
