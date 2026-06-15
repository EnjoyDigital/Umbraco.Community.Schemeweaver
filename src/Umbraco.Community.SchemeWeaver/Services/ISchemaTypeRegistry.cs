using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Registry of Schema.org types discovered from Schema.NET assembly.
/// </summary>
public interface ISchemaTypeRegistry
{
    IEnumerable<SchemaTypeInfo> GetAllTypes();
    SchemaTypeInfo? GetType(string name);
    IEnumerable<SchemaPropertyInfo> GetProperties(string typeName);
    IEnumerable<SchemaTypeInfo> Search(string query);
    Type? GetClrType(string typeName);

    /// <summary>
    /// Eagerly builds the Schema.org type cache (a one-off Schema.NET assembly
    /// scan). Safe to call multiple times — subsequent calls are no-ops. Call at
    /// application start so the first request that needs schema types doesn't pay
    /// the reflection cost on the request thread.
    /// </summary>
    void EnsureInitialised();
}

/// <summary>
/// Information about a Schema.org property.
/// </summary>
public class SchemaPropertyInfo
{
    public string Name { get; set; } = string.Empty;
    public string PropertyType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public List<string> AcceptedTypes { get; set; } = [];
    public bool IsComplexType { get; set; }
}
