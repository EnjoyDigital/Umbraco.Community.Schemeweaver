using System.Collections.Concurrent;
using System.Reflection;
using Schema.NET;
using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Singleton registry that scans Schema.NET assembly for types inheriting Thing.
/// </summary>
public class SchemaTypeRegistry : ISchemaTypeRegistry
{
    private readonly ConcurrentDictionary<string, SchemaTypeEntry> _types = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _initialised;
    private readonly object _initLock = new();

    private class SchemaTypeEntry
    {
        public Type ClrType { get; set; } = null!;
        public SchemaTypeInfo Info { get; set; } = null!;
        public List<SchemaPropertyInfo> Properties { get; set; } = [];
    }

    private void EnsureInitialised()
    {
        if (_initialised) return;

        lock (_initLock)
        {
            if (_initialised) return;

            var thingType = typeof(Thing);
            var assembly = thingType.Assembly;

            var schemaTypes = assembly.GetExportedTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && thingType.IsAssignableFrom(t));

            foreach (var type in schemaTypes)
            {
                var properties = GetSchemaProperties(type);
                var parentType = type.BaseType;
                string? parentName = parentType is not null
                    && parentType != typeof(object)
                    && thingType.IsAssignableFrom(parentType)
                        ? parentType.Name
                        : null;

                var entry = new SchemaTypeEntry
                {
                    ClrType = type,
                    Info = new SchemaTypeInfo
                    {
                        Name = type.Name,
                        Description = null,
                        ParentTypeName = parentName,
                        PropertyCount = properties.Count
                    },
                    Properties = properties
                };

                _types.TryAdd(type.Name, entry);
            }

            _initialised = true;
        }
    }

    public IEnumerable<SchemaTypeInfo> GetAllTypes()
    {
        EnsureInitialised();
        return _types.Values.Select(e => e.Info).OrderBy(t => t.Name);
    }

    public SchemaTypeInfo? GetType(string name)
    {
        EnsureInitialised();
        return _types.TryGetValue(name, out var entry) ? entry.Info : null;
    }

    public IEnumerable<SchemaPropertyInfo> GetProperties(string typeName)
    {
        EnsureInitialised();
        return _types.TryGetValue(typeName, out var entry)
            ? entry.Properties
            : [];
    }

    public IEnumerable<SchemaTypeInfo> Search(string query)
    {
        EnsureInitialised();
        if (string.IsNullOrWhiteSpace(query)) return GetAllTypes();

        var lowerQuery = query.ToLowerInvariant();
        return _types.Values
            .Where(e => e.Info.Name.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)
                     || (e.Info.Description?.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(e => e.Info)
            .OrderBy(t => t.Name);
    }

    public Type? GetClrType(string typeName)
    {
        EnsureInitialised();
        return _types.TryGetValue(typeName, out var entry) ? entry.ClrType : null;
    }

    private static List<SchemaPropertyInfo> GetSchemaProperties(Type type)
    {
        var properties = new List<SchemaPropertyInfo>();

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p is { CanRead: true, CanWrite: true });

        foreach (var prop in props)
        {
            // Skip non-schema properties
            if (prop.Name is "Context" or "Type" or "Id") continue;

            var propertyType = GetFriendlyTypeName(prop.PropertyType);
            properties.Add(new SchemaPropertyInfo
            {
                Name = prop.Name,
                PropertyType = propertyType,
                IsRequired = false
            });
        }

        return properties;
    }

    private static string GetFriendlyTypeName(Type type)
    {
        if (!type.IsGenericType) return type.Name;

        var genericArgs = type.GetGenericArguments();
        var typeName = type.Name.Split('`')[0];

        return typeName switch
        {
            "Nullable" when genericArgs.Length == 1 => $"{GetFriendlyTypeName(genericArgs[0])}?",
            "OneOrMany" or "Values" => string.Join(" | ", genericArgs.Select(GetFriendlyTypeName)),
            _ => $"{typeName}<{string.Join(", ", genericArgs.Select(GetFriendlyTypeName))}>"
        };
    }
}
