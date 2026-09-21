// One-off dumper: serialises the production SchemaTypeRegistry (every Schema.org type
// Schema.NET exposes, its parent type, and its properties) to JSON so the eval harness
// and the TypeSafe leg can use the SAME property/range data the runtime uses, without
// booting Umbraco. The registry only scans the Schema.NET assembly, so it needs no DI.
using System.Text.Json;
using Umbraco.Community.SchemeWeaver.Services;

var registry = new SchemaTypeRegistry();
registry.EnsureInitialised();

var dump = registry.GetAllTypes()
    .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
    .Select(g => g.First())
    .OrderBy(t => t.Name, StringComparer.Ordinal)
    .ToDictionary(
        t => t.Name,
        t => new
        {
            name = t.Name,
            parent = t.ParentTypeName,
            propertyCount = t.PropertyCount,
            properties = registry.GetProperties(t.Name)
                .Select(p => new
                {
                    name = p.Name,
                    propertyType = p.PropertyType,
                    acceptedTypes = p.AcceptedTypes,
                    isComplexType = p.IsComplexType,
                })
                .ToList(),
        },
        StringComparer.Ordinal);

// Default output: <repo>/eval/cache/schema-types.json, resolved from this project's
// build location so the tool works from any working directory.
var outPath = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cache", "schema-types.json"));

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllText(outPath, JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = false }));
Console.WriteLine($"Wrote {dump.Count} schema types to {outPath}");
