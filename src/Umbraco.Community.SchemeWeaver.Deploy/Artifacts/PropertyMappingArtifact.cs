namespace Umbraco.Community.SchemeWeaver.Deploy.Artifacts;

/// <summary>
/// One property-mapping row inside a <see cref="SchemaMappingArtifact"/>. Field set
/// mirrors the uSync serializer's: <c>Id</c>/<c>SchemaMappingId</c> are environment-local
/// and deliberately excluded so checksums are deterministic across environments.
/// Optional fields are normalised to <c>null</c> (never empty string) at build time
/// for the same reason. Row order is load-bearing — resolvers emit values in row order.
/// </summary>
public class PropertyMappingArtifact
{
    public string SchemaPropertyName { get; set; } = string.Empty;

    public string SourceType { get; set; } = "property";

    public bool IsAutoMapped { get; set; }

    public string? ContentTypePropertyAlias { get; set; }

    public string? SourceContentTypeAlias { get; set; }

    public string? TransformType { get; set; }

    public string? StaticValue { get; set; }

    public string? NestedSchemaTypeName { get; set; }

    /// <summary>Opaque resolver JSON, carried verbatim (see SchemaMappingArtifact remarks).</summary>
    public string? ResolverConfig { get; set; }

    /// <summary>Opaque dynamic-root JSON, carried verbatim (see SchemaMappingArtifact remarks).</summary>
    public string? DynamicRootConfig { get; set; }

    public string? TargetPieceKey { get; set; }
}
