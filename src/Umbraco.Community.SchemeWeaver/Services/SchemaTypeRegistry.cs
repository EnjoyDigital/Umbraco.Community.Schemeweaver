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

            // Second pass: set IsComplexType now that _types is fully populated
            foreach (var entry2 in _types.Values)
            {
                foreach (var prop in entry2.Properties)
                {
                    prop.IsComplexType = prop.AcceptedTypes.Any(t => _types.ContainsKey(t));
                }
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
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true })
            .GroupBy(p => p.Name)
            .Select(g => g.First())
            .Where(p => p.Name is not ("Context" or "Type" or "Id"))
            .Select(prop => new SchemaPropertyInfo
            {
                Name = prop.Name,
                PropertyType = GetFriendlyTypeName(prop.PropertyType),
                IsRequired = false,
                AcceptedTypes = GetAcceptedTypes(prop.PropertyType),
                // IsComplexType set in second pass
            })
            .ToList();
    }

    private static List<string> GetAcceptedTypes(Type type)
    {
        var result = new List<string>();
        CollectLeafTypes(type, result);
        return result.Distinct().ToList();
    }

    private static void CollectLeafTypes(Type type, List<string> result)
    {
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var name = genericDef.Name.Split('`')[0];

            if (name is "OneOrMany" or "Values" or "Nullable")
            {
                foreach (var arg in type.GetGenericArguments())
                    CollectLeafTypes(arg, result);
                return;
            }
        }

        if (type.IsInterface && typeof(Schema.NET.IThing).IsAssignableFrom(type))
        {
            // Strip 'I' prefix: IOrganization → Organization
            var typeName = type.Name.StartsWith('I') ? type.Name.Substring(1) : type.Name;
            result.Add(typeName);
            return;
        }

        // Simple type
        result.Add(type.Name switch
        {
            "String" => "String",
            "Uri" => "Uri",
            "DateTime" => "DateTime",
            "DateTimeOffset" => "DateTime",
            "Int32" => "Integer",
            "Decimal" => "Number",
            "Double" => "Number",
            "Boolean" => "Boolean",
            _ => type.Name
        });
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
