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
    public bool IsInherited { get; set; }

    /// <summary>
    /// Optional @id template for this mapping. Tokens: {url}, {type}, {key},
    /// {culture}, {siteUrl}. When null the generator uses the default
    /// {url}#{type} convention.
    /// </summary>
    public string? IdOverride { get; set; }

    public List<PropertyMappingDto> PropertyMappings { get; set; } = [];

    /// <summary>
    /// Output-only. How this mapped content type can emit JSON-LD:
    /// <c>routed-page</c>, <c>composed-from-block</c> or <c>unknown</c>. Set by
    /// the service on read/save; ignored on input (never written back to the
    /// entity).
    /// </summary>
    public string? Reachability { get; set; }

    /// <summary>
    /// Output-only. Structural warnings about this mapping (e.g. a property
    /// mapped to an object type outside its Schema.org range, which would be
    /// silently dropped at generation time). Set by the service on single read
    /// and save; ignored on input.
    /// </summary>
    public List<ValidationIssueDto> Warnings { get; set; } = [];

    /// <summary>
    /// Output-only. Disk/DB drift for this mapping's uSync <c>.config</c>: a
    /// <c>MappingDriftStatus</c> code (<c>in-sync</c>, <c>db-only</c>,
    /// <c>content-differs</c>, or <c>usync-unavailable</c> when the addon isn't
    /// installed). Set by the service on read/save; ignored on input.
    /// </summary>
    public string? DriftStatus { get; set; }

    /// <summary>
    /// Output-only (save response). Where the mapping was persisted:
    /// <c>database</c> (DB only) or <c>database+usync</c> (also written to disk).
    /// </summary>
    public string? PersistedTo { get; set; }
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
    public string? ResolverConfig { get; set; }
    public string? DynamicRootConfig { get; set; }

    /// <summary>
    /// For <c>reference</c> source type: the key of the graph piece whose @id
    /// this property should resolve to (e.g. <c>"organization"</c>).
    /// </summary>
    public string? TargetPieceKey { get; set; }
}
