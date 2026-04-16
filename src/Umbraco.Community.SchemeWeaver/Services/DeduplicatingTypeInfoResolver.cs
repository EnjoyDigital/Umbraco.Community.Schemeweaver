using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Schema.NET;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// A <see cref="IJsonTypeInfoResolver"/> that works around property name collisions in
/// Schema.NET's interface hierarchy (e.g. <c>IDrug.Funding</c>, <c>IArchiveOrganization.Address</c>).
/// When the default resolver throws because two interfaces expose the same JSON property name,
/// this resolver rebuilds the type info and keeps only the first occurrence.
/// </summary>
internal sealed class DeduplicatingTypeInfoResolver : IJsonTypeInfoResolver
{
    private readonly DefaultJsonTypeInfoResolver _default = new();

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        try
        {
            return _default.GetTypeInfo(type, options);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("collides"))
        {
            return BuildDeduplicatedTypeInfo(type, options);
        }
    }

    private static JsonTypeInfo BuildDeduplicatedTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = JsonTypeInfo.CreateJsonTypeInfo(type, options);
        typeInfo.CreateObject = () => Activator.CreateInstance(type)!;

        var seen = new HashSet<string>();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var jsonName = ResolveJsonPropertyName(prop, options);
            if (!seen.Add(jsonName))
                continue;

            try
            {
                var propInfo = typeInfo.CreateJsonPropertyInfo(prop.PropertyType, jsonName);
                propInfo.Get = prop.CanRead ? prop.GetValue : null;
                propInfo.Set = prop.CanWrite ? prop.SetValue : null;
                typeInfo.Properties.Add(propInfo);
            }
            catch
            {
                // Skip properties that can't be configured
            }
        }

        return typeInfo;
    }

    private static string ResolveJsonPropertyName(PropertyInfo prop, JsonSerializerOptions options)
    {
        var attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (attr is not null)
            return attr.Name;

        return options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;
    }
}
