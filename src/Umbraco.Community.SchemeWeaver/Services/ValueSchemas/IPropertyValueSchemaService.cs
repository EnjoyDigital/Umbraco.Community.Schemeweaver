namespace Umbraco.Community.SchemeWeaver.Services.ValueSchemas;

/// <summary>
/// Exposes Umbraco 17.4+'s per-data-type value JSON Schema (the shape a property's stored value
/// takes — types, maxLength, UUID/crop structure, ranges) to SchemeWeaver's mapping logic and MCP
/// surface. Wraps the core <c>IPropertyEditorSchemaService</c>, resolved optionally so a host that
/// predates 17.4 (the package floor is 17.0) degrades to <c>null</c> rather than failing — every
/// caller falls back to its editor-alias behaviour when the schema is unavailable.
/// </summary>
public interface IPropertyValueSchemaService
{
    /// <summary>True when the underlying Umbraco schema service is available (host is 17.4+).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The JSON Schema (draft 2020-12) for the value of the data type with the given key, serialised
    /// to a compact JSON string — or <c>null</c> when the host predates 17.4, the data type is
    /// unknown, or its editor does not provide a schema. Results are cached per data-type key.
    /// </summary>
    Task<string?> GetDataTypeValueSchemaAsync(Guid dataTypeKey);
}
