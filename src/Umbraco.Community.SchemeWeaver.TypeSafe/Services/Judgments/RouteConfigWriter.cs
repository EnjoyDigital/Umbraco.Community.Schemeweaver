using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

/// <summary>
/// Serialises a block plan to the resolver-config JSON the core's <c>BlockContentResolver</c>
/// parses (<c>ResolverConfigModel</c>, case-insensitively). Two shapes: the per-element-type
/// <c>routes</c> form, which nests recursively for blocks inside blocks, and the v1
/// <c>nestedMappings</c> form, kept byte-identical to what the mapper first shipped so a
/// single-element list produces exactly the config it did before routes existed.
/// </summary>
/// <remarks>
/// The key order and the null omission are part of what "byte-identical" means: camelCase
/// keys, nulls left out, <c>wrapInType</c> after <c>contentProperty</c>. Schema property names
/// keep the registry's PascalCase (<c>Name</c>, <c>AcceptedAnswer</c>): the core resolves them
/// case-insensitively and the v1 output carried them that way, so changing the case would
/// churn every saved mapping's config for no behavioural gain.
/// </remarks>
internal static class RouteConfigWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The <c>{"routes":[...]}</c> form: one route per kept element type, nesting recursively.</summary>
    public static string WriteRoutes(IReadOnlyList<PlannedRoute> routes)
        => JsonSerializer.Serialize(new RoutesShape(routes.Select(ToShape).ToList()), Json);

    /// <summary>The v1 <c>{"nestedMappings":[...]}</c> form: one flat field list for the whole list.</summary>
    public static string WriteNestedMappings(IReadOnlyList<PlannedFieldMapping> mappings)
        => JsonSerializer.Serialize(new NestedMappingsShape(mappings.Select(ToShape).ToList()), Json);

    private static RouteShape ToShape(PlannedRoute route)
        => new(route.BlockAlias, route.NestedSchemaType, route.PropertyMappings.Select(ToShape).ToList());

    private static FieldMappingShape ToShape(PlannedFieldMapping mapping)
        => new(
            mapping.SchemaProperty,
            mapping.ContentProperty,
            mapping.WrapInType,
            mapping.Routes is { Count: > 0 } routes ? routes.Select(ToShape).ToList() : null,
            mapping.ExtractAs,
            mapping.NestedContentProperty);

    // ---- wire shapes (camelCase is pinned here, not left to the caller's serializer options) ----

    private sealed record RoutesShape(
        [property: JsonPropertyName("routes")] IReadOnlyList<RouteShape> Routes);

    private sealed record NestedMappingsShape(
        [property: JsonPropertyName("nestedMappings")] IReadOnlyList<FieldMappingShape> NestedMappings);

    private sealed record RouteShape(
        [property: JsonPropertyName("blockAlias")] string BlockAlias,
        [property: JsonPropertyName("nestedSchemaType")] string NestedSchemaType,
        [property: JsonPropertyName("propertyMappings")] IReadOnlyList<FieldMappingShape> PropertyMappings);

    private sealed record FieldMappingShape(
        [property: JsonPropertyName("schemaProperty")] string SchemaProperty,
        [property: JsonPropertyName("contentProperty")] string ContentProperty,
        [property: JsonPropertyName("wrapInType")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WrapInType,
        [property: JsonPropertyName("routes")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RouteShape>? Routes,
        [property: JsonPropertyName("extractAs")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExtractAs,
        [property: JsonPropertyName("nestedContentProperty")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NestedContentProperty);
}

/// <summary>One per-element-type route: the element alias, its nested Schema.org type and its field mappings.</summary>
internal sealed record PlannedRoute(
    string BlockAlias,
    string NestedSchemaType,
    IReadOnlyList<PlannedFieldMapping> PropertyMappings);

/// <summary>
/// One field of an element type mapped onto a property of the element's nested type. Exactly
/// one of three forms: a scalar (optionally wrapped in <see cref="WrapInType"/>), a nested block
/// list planned as <see cref="Routes"/>, or a nested block list flattened as a string list
/// (<see cref="ExtractAs"/> with <see cref="NestedContentProperty"/>).
/// </summary>
internal sealed record PlannedFieldMapping(
    string SchemaProperty,
    string ContentProperty,
    string? WrapInType = null,
    IReadOnlyList<PlannedRoute>? Routes = null,
    string? ExtractAs = null,
    string? NestedContentProperty = null);
